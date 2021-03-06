﻿using imageStacker.Core.Extensions;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace imageStacker.Core
{
    public class StackAllStrategy<T> : ThreadProcessingStrategy<T>, IImageProcessingStrategy<T> where T : IProcessableImage
    {
        private const int tasksCount = 4;

        public StackAllStrategy(ILogger logger, IMutableImageFactory<T> factory) : base(logger, factory)
        { }

        protected override async Task ProcessingThread(List<IFilter<T>> filters)
        {
            T firstMutableImage = await GetFirstImage();
            var baseImages = filters.Select((filter, index) => (filter, image: factory.Clone(firstMutableImage), index)).ToList();

            var tasks = new List<Task>();

            while (true)
            {
                await tasks.WaitForFinishingTasks(tasksCount);

                var (cancelled, nextImage) = await inputQueue.TryDequeueOrWait(inputFinishedToken);

                if (cancelled)
                {
                    break;
                }

                baseImages.ForEach(data =>
                {
                    var task = Task.Factory.StartNew(() => data.filter.Process(data.image, nextImage));
                    tasks.Add(task);
                    task.Start();
                });
            }

            await Task.WhenAll(tasks);
            baseImages.ForEach(data => outputQueue.Append((data.image, new SaveInfo(null, data.filter.Name))));
        }
    }
}
