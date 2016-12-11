﻿using System;
using System.Threading;
using NUnit.Framework;
using Shuttle.Core.Infrastructure;

namespace Shuttle.Esb.Tests
{
    public class DeferredFixture : IntegrationFixture
    {
        private readonly ILog _log;

        public DeferredFixture()
        {
            _log = Log.For(this);
        }

        protected void TestDeferredProcessing(string queueUriFormat, bool isTransactional)
        {
            const int deferredMessageCount = 10;
            const int millisecondsToDefer = 500;

            var configuration = GetInboxConfiguration(queueUriFormat, 1, isTransactional);

            var container = new DefaultComponentContainer();

            var defaultConfigurator = new DefaultConfigurator(container);

            defaultConfigurator.DontRegister<DeferredMessageModule>();

            defaultConfigurator.RegisterComponents(configuration);

            var module = new DeferredMessageModule(container.Resolve<IPipelineFactory>(), deferredMessageCount);

            container.Register(module.GetType(), module);

            using (var bus = ServiceBus.Create(container))
            {
                bus.Start();

                var ignoreTillDate = DateTime.Now.AddSeconds(5);

                for (var i = 0; i < deferredMessageCount; i++)
                {
                    EnqueueDeferredMessage(configuration, container.Resolve<ITransportMessageFactory>(), container.Resolve<ISerializer>(), ignoreTillDate);

                    ignoreTillDate = ignoreTillDate.AddMilliseconds(millisecondsToDefer);
                }

                // add the extra time else there is no time to process message being returned
                var timeout = ignoreTillDate.AddSeconds(150);
                var timedOut = false;

                _log.Information(string.Format("[start wait] : now = '{0}'", DateTime.Now));

                // wait for the message to be returned from the deferred queue
                while (!module.AllMessagesHandled()
                       &&
                       !timedOut)
                {
                    Thread.Sleep(millisecondsToDefer);

                    timedOut = timeout < DateTime.Now;
                }

                _log.Information(string.Format("[end wait] : now = '{0}' / timeout = '{1}' / timed out = '{2}'", DateTime.Now,
                    timeout, timedOut));

                _log.Information(string.Format("{0} of {1} deferred messages returned to the inbox.",
                    module.NumberOfDeferredMessagesReturned, deferredMessageCount));
                _log.Information(string.Format("{0} of {1} deferred messages handled.", module.NumberOfMessagesHandled,
                    deferredMessageCount));

                Assert.IsTrue(module.AllMessagesHandled(), "All the deferred messages were not handled.");

                Assert.IsTrue(configuration.Inbox.ErrorQueue.IsEmpty());
                Assert.IsNull(configuration.Inbox.DeferredQueue.GetMessage());
                Assert.IsNull(configuration.Inbox.WorkQueue.GetMessage());
            }

            AttemptDropQueues(queueUriFormat);
        }

        private void EnqueueDeferredMessage(IServiceBusConfiguration configuration, ITransportMessageFactory transportMessageFactory, ISerializer serializer, DateTime ignoreTillDate)
        {
            var command = new SimpleCommand
            {
                Name = Guid.NewGuid().ToString()
            };

            var message = transportMessageFactory.Create(command, c => c
                .Defer(ignoreTillDate)
                .WithRecipient(configuration.Inbox.WorkQueue), null);

            configuration.Inbox.WorkQueue.Enqueue(message, serializer.Serialize(message));

            _log.Information(string.Format("[message enqueued] : name = '{0}' / deferred till date = '{1}'", command.Name,
                message.IgnoreTillDate));
        }

        private static ServiceBusConfiguration GetInboxConfiguration(string queueUriFormat, int threadCount,
            bool isTransactional)
        {
            using (var queueManager = GetQueueManager())
            {
                var configuration = DefaultConfiguration(isTransactional);

                var inboxWorkQueue = queueManager.GetQueue(string.Format(queueUriFormat, "test-inbox-work"));
                var inboxDeferredQueue = queueManager.GetQueue(string.Format(queueUriFormat, "test-inbox-deferred"));
                var errorQueue = queueManager.GetQueue(string.Format(queueUriFormat, "test-error"));

                configuration.Inbox =
                    new InboxQueueConfiguration
                    {
                        WorkQueue = inboxWorkQueue,
                        DeferredQueue = inboxDeferredQueue,
                        ErrorQueue = errorQueue,
                        DurationToSleepWhenIdle = new[] {TimeSpan.FromMilliseconds(5)},
                        ThreadCount = threadCount
                    };

                inboxWorkQueue.AttemptDrop();
                inboxDeferredQueue.AttemptDrop();
                errorQueue.AttemptDrop();

                queueManager.CreatePhysicalQueues(configuration);

                inboxWorkQueue.AttemptPurge();
                inboxDeferredQueue.AttemptPurge();
                errorQueue.AttemptPurge();

                return configuration;
            }
        }
    }
}