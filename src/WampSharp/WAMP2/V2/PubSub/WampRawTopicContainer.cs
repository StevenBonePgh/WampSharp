﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using WampSharp.V2.Binding;
using WampSharp.V2.Core;
using WampSharp.V2.Core.Contracts;
using WampSharp.V2.Core.Listener;

namespace WampSharp.V2.PubSub
{
    internal class WampRawTopicContainer<TMessage> : IWampRawTopicContainer<TMessage>
    {
        private readonly IWampTopicContainer mTopicContainer;
        private readonly IWampEventSerializer<TMessage> mEventSerializer;
        private readonly IWampBinding<TMessage> mBinding;
        private readonly object mLock = new object();

        private readonly WampIdMapper<RawWampTopic<TMessage>> mSubscriptionIdToTopic =
            new WampIdMapper<RawWampTopic<TMessage>>();

        private readonly ConcurrentDictionary<string, RawWampTopic<TMessage>> mTopicUriToTopic =
            new ConcurrentDictionary<string, RawWampTopic<TMessage>>();

        public WampRawTopicContainer(IWampTopicContainer topicContainer,
                                     IWampEventSerializer<TMessage> eventSerializer,
                                     IWampBinding<TMessage> binding)
        {
            mTopicContainer = topicContainer;
            mEventSerializer = eventSerializer;
            mBinding = binding;
        }

        public long Subscribe(ISubscribeRequest<TMessage> request, TMessage options, string topicUri)
        {
            lock (mLock)
            {
                RawWampTopic<TMessage> rawTopic;

                if (!mTopicUriToTopic.TryGetValue(topicUri, out rawTopic))
                {
                    rawTopic = CreateRawTopic(topicUri);
                    
                    IDisposable disposable =
                        mTopicContainer.Subscribe(rawTopic, options, topicUri);

                    rawTopic.SubscriptionDisposable = disposable;
                }

                rawTopic.Subscribe(request, options);
                
                return rawTopic.SubscriptionId;
            }
        }

        public void Unsubscribe(IUnsubscribeRequest<TMessage> request, long subscriptionId)
        {
            lock (mLock)
            {
                RawWampTopic<TMessage> rawTopic;

                if (!mSubscriptionIdToTopic.TryGetValue(subscriptionId, out rawTopic))
                {
                    throw new WampException(WampErrors.NoSuchSubscription, subscriptionId);
                }

                rawTopic.Unsubscribe(request);
            }
        }

        public long Publish(TMessage options, string topicUri)
        {
            return mTopicContainer.Publish(options, topicUri);
        }

        public long Publish(TMessage options, string topicUri, TMessage[] arguments)
        {
            object[] castedArguments = arguments.Cast<object>().ToArray();
            return mTopicContainer.Publish(options, topicUri, castedArguments);
        }

        public long Publish(TMessage options, string topicUri, TMessage[] arguments, TMessage argumentKeywords)
        {
            object[] castedArguments = arguments.Cast<object>().ToArray();
            return mTopicContainer.Publish(options, topicUri, castedArguments, argumentKeywords);
        }

        private void OnTopicEmpty(object sender, EventArgs e)
        {
            RawWampTopic<TMessage> rawTopic = sender as RawWampTopic<TMessage>;

            if (rawTopic != null)
            {
                lock (mLock)
                {
                    if (!rawTopic.HasSubscribers)
                    {
                        mSubscriptionIdToTopic.TryRemove(rawTopic.SubscriptionId, out rawTopic);
                        mTopicUriToTopic.TryRemove(rawTopic.TopicUri, out rawTopic);
                        rawTopic.Dispose();
                    }
                }
            }
        }

        private RawWampTopic<TMessage> CreateRawTopic(string topicUri)
        {
            RawWampTopic<TMessage> newTopic =
                new RawWampTopic<TMessage>(topicUri,
                                           mEventSerializer,
                                           mBinding);

            long subscriptionId =
                mSubscriptionIdToTopic.Add(newTopic);

            newTopic.SubscriptionId = subscriptionId;

            mTopicUriToTopic.TryAdd(topicUri, newTopic);

            newTopic.TopicEmpty += OnTopicEmpty;

            return newTopic;
        }
    }
}