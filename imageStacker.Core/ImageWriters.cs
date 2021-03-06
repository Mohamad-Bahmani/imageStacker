﻿using System;
using System.Drawing.Imaging;
using System.IO;

namespace imageStacker.Core
{
    public interface ISaveInfo
    {
        int? Index { get; }

        string Filtername { get; }
    }

    public class SaveInfo : ISaveInfo
    {
        public SaveInfo(int? index, string filtername)
        {
            Index = index;
            Filtername = filtername;
        }

        public int? Index { get; }

        public string Filtername { get; }

    }

    public interface IImageWriter<T> where T : IProcessableImage
    {
        public void Writefile(T image, ISaveInfo info);
    }

    public abstract class ImageWriter<T> : IImageWriter<T> where T : IProcessableImage
    {
        protected readonly ILogger logger;
        protected readonly IMutableImageFactory<T> factory;
        public ImageWriter(ILogger logger, IMutableImageFactory<T> factory)
        {
            this.logger = logger;
            this.factory = factory;
        }
        public abstract void Writefile(T image, ISaveInfo info);
    }

    public class ImageFileWriter<T> : IImageWriter<T> where T : IProcessableImage
    {
        private readonly string Filename, OutputFolder;
        private readonly IMutableImageFactory<T> Factory;

        public ImageFileWriter(string filename, string outputFolder, IMutableImageFactory<T> factory)
        {
            Filename = filename;
            OutputFolder = outputFolder;
            Factory = factory;
        }

        public void Writefile(T image, ISaveInfo info)
        {
            string path = Path.Combine(OutputFolder,
                string.Join('-',
                    Filename,
                    info.Filtername,
                    info.Index.HasValue ? info.Index.Value.ToString("d6") : string.Empty) + ".jpg");
            File.Delete(path);
            Factory.ToImage(image).Save(path, ImageFormat.Jpeg);
        }
    }

    /// <summary>
    /// Outputs raw RGB Byte Stream
    /// </summary>
    public class ImageStreamWriter<T> : ImageWriter<T>, IDisposable where T : IProcessableImage
    {
        private readonly Stream outputStream;
        public ImageStreamWriter(ILogger logger, IMutableImageFactory<T> factory, Stream outputStream)
            : base(logger, factory)
        {
            this.outputStream = outputStream;
        }

        public void Dispose()
        {
            outputStream?.Close();
        }

        public override void Writefile(T image, ISaveInfo info)
        {
            var imageAsBytes = factory.ToBytes(image);
            outputStream.Write(imageAsBytes, 0, imageAsBytes.Length);
        }

        ~ImageStreamWriter()
        {
            outputStream?.Close();
        }
    }

    public class TestImageWriter<T> : ImageWriter<T> where T : IProcessableImage
    {
        public TestImageWriter(ILogger logger, IMutableImageFactory<T> factory)
         : base(logger, factory)
        {
        }
        public override void Writefile(T image, ISaveInfo info)
        {
        }
    }
}
