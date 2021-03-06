using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WampSharp.Core.Listener;
using WampSharp.Core.Serialization;
using WampSharp.V2.Core;
using WampSharp.V2.Core.Contracts;

namespace WampSharp.V2.Rpc
{
    internal class WampCalleeRpcOperation<TMessage> : RemoteWampCalleeDetails, IRemoteWampCalleeOperation, IDisposable
    {
        private readonly ManualResetEvent mResetEvent = new ManualResetEvent(false);
        private readonly IWampCalleeInvocationHandler<TMessage> mHandler;
        private readonly WampCalleeOperationCatalog<TMessage> mCatalog;
        private long mClientDisconnected = 0;

        public WampCalleeRpcOperation(string procedure,
                                      IWampCallee callee,
                                      RegisterOptions options,
                                      IWampCalleeInvocationHandler<TMessage> handler,
                                      WampCalleeOperationCatalog<TMessage> catalog) :
                                          base(callee, procedure, options)
        {
            mHandler = handler;
            mCatalog = catalog;
        }

        private void OnClientDisconnect(object sender, EventArgs e)
        {
            OnDisconnect();
        }

        private void OnDisconnect()
        {
            if (Interlocked.CompareExchange(ref mClientDisconnected, 1, 0) == 0)
            {
                IWampConnectionMonitor monitor = Callee as IWampConnectionMonitor;
                monitor.ConnectionClosed -= OnClientDisconnect;

                mCatalog.Unregister(Callee, RegistrationId);
                mHandler.Unregistered(this);                
            }
        }

        public void Invoke<TOther>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TOther> formatter, InvocationDetails details)
        {
            this.Invoke(caller, details);
        }

        public void Invoke<TOther>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TOther> formatter, InvocationDetails details, TOther[] arguments)
        {
            this.Invoke(caller, details, arguments.Cast<object>().ToArray());
        }

        public void Invoke<TOther>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TOther> formatter, InvocationDetails details, TOther[] arguments, IDictionary<string, TOther> argumentsKeywords)
        {
            this.Invoke(caller, details, arguments.Cast<object>().ToArray(), argumentsKeywords.ToDictionary(x => x.Key, x => (object)x.Value));
        }

        public void Invoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails details)
        {
            InvokePattern(caller, details, invocationDetails => InnerInvoke(caller, invocationDetails));
        }

        public void Invoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails details, object[] arguments)
        {
            InvokePattern(caller, details, invocationDetails => InnerInvoke(caller, invocationDetails, arguments));
        }

        public void Invoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails details, object[] arguments, IDictionary<string, object> argumentsKeywords)
        {
            InvokePattern(caller, details, invocationDetails => InnerInvoke(caller, invocationDetails, arguments, argumentsKeywords));
        }

        private void InnerInvoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails options)
        {
            long requestId =
                mHandler.RegisterInvocation(this, caller, options);

            Callee.Invocation(requestId, RegistrationId, options);
        }

        private void InnerInvoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails options, object[] arguments)
        {
            long requestId =
                mHandler.RegisterInvocation(this, caller, options, arguments);

            Callee.Invocation(requestId, RegistrationId, options, arguments);
        }

        private void InnerInvoke(IWampRawRpcOperationRouterCallback caller, InvocationDetails options, object[] arguments,
                                 IDictionary<string, object> argumentsKeywords)
        {
            long requestId =
                mHandler.RegisterInvocation(this, caller, options, arguments, argumentsKeywords);

            Callee.Invocation(requestId, RegistrationId, options, arguments, argumentsKeywords);
        }

        private void InvokePattern(IWampRawRpcOperationRouterCallback caller, InvocationDetails details,
                                   Action<InvocationDetails> action)
        {
            mResetEvent.WaitOne();

            if (Interlocked.Read(ref mClientDisconnected) == 0)
            {
                var detailsForCallee = GetInvocationDetails(details);
                action(detailsForCallee);
            }
            else
            {
                caller.Error(WampObjectFormatter.Value,
                             new Dictionary<string, string>(),
                             WampErrors.CalleeDisconnected);
            }
        }

        private InvocationDetails GetInvocationDetails(InvocationDetails details)
        {
            InvocationDetailsExtended casted = details as InvocationDetailsExtended;

            if (casted == null)
            {
                return details;
            }

            InvocationDetails result = new InvocationDetails(casted);

            CallOptions callerOptions = casted.CallerOptions;

            if (Options.DiscloseCaller == true &&
                callerOptions.DiscloseMe == false)
            {
                throw new WampException(WampErrors.DiscloseMeNotAllowed);
            }

            if (Options.DiscloseCaller == true ||
                callerOptions.DiscloseMe == true)
            {
                result.Caller = casted.CallerSession;

                result.AuthenticationId = casted.AuthenticationId;
                result.AuthenticationMethod = casted.AuthenticationMethod;
                result.AuthenticationRole = casted.AuthenticationRole;
            }

            if (callerOptions.ReceiveProgress == true)
            {
                result.ReceiveProgress = true;
            }

            if (Options.Match != WampMatchPattern.Exact)
            {
                result.Procedure = casted.ProcedureUri;
            }

            return result;
        }

        public void Open()
        {
            IWampConnectionMonitor monitor = Callee as IWampConnectionMonitor;            

            monitor.ConnectionClosed += OnClientDisconnect;

            mResetEvent.Set();
        }

        public void Dispose()
        {
            IWampConnectionMonitor monitor = Callee as IWampConnectionMonitor;
            monitor.ConnectionClosed -= OnClientDisconnect;
        }
    }
}