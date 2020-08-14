﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace imageStacker.Core.Readers
{
    public class ImageMutliFileOrderedReader<T> : ImageReaderBase<T> where T : IProcessableImage
    {
        public ImageMutliFileOrderedReader(ILogger logger, IMutableImageFactory<T> factory, string folderName, string filter = "*.JPG")
            : base(logger, factory)
        {
            this.filenames = new Queue<string>(Directory.GetFiles(folderName, filter, new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.System,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            }));
            this.logger.WriteLine($"Items found: {filenames.Count}", Verbosity.Info);
        }
        public ImageMutliFileOrderedReader(ILogger logger, IMutableImageFactory<T> factory, string[] files)
            : base(logger, factory)
        {
            this.filenames = new Queue<string>(files);
            this.logger.WriteLine($"Items found: {filenames.Count}", Verbosity.Info);
        }

        private const int processQueueLength = 8;
        private const int publishQueueLength = 8;
        private const int readQueueLength = 8;

        private readonly Queue<string> filenames;

        private readonly CancellationTokenSource readingFinished = new CancellationTokenSource();
        private readonly CancellationTokenSource parsingFinished = new CancellationTokenSource();

        private readonly ConcurrentQueue<(MemoryStream, int)> dataToParse = new ConcurrentQueue<(MemoryStream, int)>();
        private readonly ConcurrentQueue<(T, int)> dataToPublish = new ConcurrentQueue<(T, int)>();
        private async Task ReadFromDisk()
        {
            int i = 0;
            foreach (var filename in filenames)
            {
                try
                {
                    logger.NotifyFillstate(dataToParse.Count, "ReadBuffer");
                    logger.NotifyFillstate(i, "FilesRead");
                    dataToParse.Enqueue((new MemoryStream(File.ReadAllBytes(filename), false), i));
                    i++;
                }
                catch (Exception e) { Console.WriteLine(e); }
                while (dataToParse.Count > readQueueLength)
                {
                    await Task.Delay(100);
                    await Task.Yield();
                }
            }
            readingFinished.Cancel();
        }

        private async Task DecodeImage(ConcurrentQueue<(T, int)> queue)
        {
            while (!readingFinished.Token.IsCancellationRequested || !dataToParse.IsEmpty)
            {
                if (!dataToParse.TryDequeue(out var data))
                {
                    await Task.Delay(100);
                    await Task.Yield();
                    continue;
                }
                var bmp = new Bitmap(data.Item1);
                var width = bmp.Width;
                var height = bmp.Height;
                var pixelFormat = bmp.PixelFormat;

                var bmp1Data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, pixelFormat);
                var length = bmp1Data.Stride * bmp1Data.Height;

                byte[] bmp1Bytes = new byte[length];
                Marshal.Copy(bmp1Data.Scan0, bmp1Bytes, 0, length);

                queue.Enqueue((factory.FromBytes(width, height, bmp1Bytes), data.Item2));

                bmp.UnlockBits(bmp1Data);
                bmp.Dispose();
                data.Item1.Dispose();
                logger.NotifyFillstate(dataToParse.Count, "ParseBuffer");

                while (queue.Count > processQueueLength)
                {
                    await Task.Delay(100);
                    await Task.Yield();
                }
            }
            parsingFinished.Cancel();
        }

        private async Task Publish(ConcurrentQueue<T> queue)
        {
            int nextImageIndex = 0;
            while (!parsingFinished.Token.IsCancellationRequested || !dataToPublish.IsEmpty)
            {
                logger.NotifyFillstate(dataToPublish.Count, "Publishbuffer");
                if (dataToPublish.Count == 0)
                {
                    await Task.Delay(100);
                    await Task.Yield();
                    continue;
                }

                for (int i = 0; i < dataToPublish.Count; i++)
                {
                    if (dataToPublish.TryDequeue(out var result))
                    {
                        if (result.Item2 == nextImageIndex)
                        {
                            nextImageIndex++;
                            queue.Enqueue(result.Item1);
                            continue;
                        }
                        dataToPublish.Enqueue(result);
                    }
                }

                while (queue.Count > publishQueueLength)
                {
                    await Task.Delay(100);
                    await Task.Yield();
                }
            }
        }

        public async override Task Produce(ConcurrentQueue<T> queue)
        {
            var readingTask = Task.Run(() => ReadFromDisk());
            var decodingTasks = Enumerable.Range(0, 6).Select(x => (Task)Task.Run(() => DecodeImage(dataToPublish)));
            var orderingTask = Task.Run(() => Publish(queue));

            await Task.WhenAll(Task.WhenAll(decodingTasks), orderingTask, readingTask);

            if (decodingTasks.Any(x => x.IsFaulted) || readingTask.IsFaulted || orderingTask.IsFaulted)
            {
                throw decodingTasks.FirstOrDefault(x => x.Exception != null)?.Exception
                      ?? readingTask.Exception
                      ?? orderingTask.Exception
                      ?? new Exception("unkown stuff happened");
            }

        }


    }

}
