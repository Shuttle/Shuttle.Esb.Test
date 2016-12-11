using System;
using System.Threading;
using NUnit.Framework;
using Shuttle.Core.Infrastructure;

namespace Shuttle.Esb.Tests
{
	public class PipelineExceptionFixture : IntegrationFixture
	{
		protected void TestExceptionHandling(string queueUriFormat)
		{
			var configuration = DefaultConfiguration(true);

		    var container = new DefaultComponentContainer();

		    var configurator = new DefaultConfigurator(container);

		    configurator.DontRegister<ReceivePipelineExceptionModule>();

            configurator.RegisterComponents(configuration);

		    var queueManager = container.Resolve<IQueueManager>();

            queueManager.ScanForQueueFactories();

			var inboxWorkQueue = queueManager.GetQueue(string.Format(queueUriFormat, "test-inbox-work"));
			var inboxErrorQueue = queueManager.GetQueue(string.Format(queueUriFormat, "test-error"));

			configuration.Inbox =
				new InboxQueueConfiguration
				{
					WorkQueue = inboxWorkQueue,
					ErrorQueue = inboxErrorQueue,
					DurationToSleepWhenIdle = new[] {TimeSpan.FromMilliseconds(5)},
					DurationToIgnoreOnFailure = new[] {TimeSpan.FromMilliseconds(5)},
					MaximumFailureCount = 100,
					ThreadCount = 1
				};


			inboxWorkQueue.Drop();
			inboxErrorQueue.Drop();

			queueManager.CreatePhysicalQueues(configuration);

			var module = new ReceivePipelineExceptionModule(inboxWorkQueue);

            container.Register(module.GetType(), module);

            var transportMessageFactory = container.Resolve<ITransportMessageFactory>();
            var serializer = container.Resolve<ISerializer>();

            using (var bus = ServiceBus.Create(container))
			{
				var message = transportMessageFactory.Create(new ReceivePipelineCommand(), c => c.WithRecipient(inboxWorkQueue));

				inboxWorkQueue.Enqueue(message, serializer.Serialize(message));

				Assert.IsFalse(inboxWorkQueue.IsEmpty());

				bus.Start();

				while (module.ShouldWait())
				{
					Thread.Sleep(10);
				}
			}
		}
	}
}