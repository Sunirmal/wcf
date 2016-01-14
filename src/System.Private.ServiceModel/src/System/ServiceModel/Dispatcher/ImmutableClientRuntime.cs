// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime;
using System.ServiceModel.Channels;

namespace System.ServiceModel.Dispatcher
{
    internal class ImmutableClientRuntime
    {
        private int _correlationCount;
        private bool _addTransactionFlowProperties;
        private IInteractiveChannelInitializer[] _interactiveChannelInitializers;
        private IClientOperationSelector _operationSelector;
        private IChannelInitializer[] _channelInitializers;
        private IClientMessageInspector[] _messageInspectors;
        private Dictionary<string, ProxyOperationRuntime> _operations;
        private ProxyOperationRuntime _unhandled;
        private bool _useSynchronizationContext;
        private bool _validateMustUnderstand;

        internal ImmutableClientRuntime(ClientRuntime behavior)
        {
            _channelInitializers = EmptyArray<IChannelInitializer>.ToArray(behavior.ChannelInitializers);
            _interactiveChannelInitializers = EmptyArray<IInteractiveChannelInitializer>.ToArray(behavior.InteractiveChannelInitializers);
            _messageInspectors = EmptyArray<IClientMessageInspector>.ToArray(behavior.MessageInspectors);

            _operationSelector = behavior.OperationSelector;
            _useSynchronizationContext = behavior.UseSynchronizationContext;
            _validateMustUnderstand = behavior.ValidateMustUnderstand;

            _unhandled = new ProxyOperationRuntime(behavior.UnhandledClientOperation, this);

            _addTransactionFlowProperties = behavior.AddTransactionFlowProperties;

            _operations = new Dictionary<string, ProxyOperationRuntime>();

            for (int i = 0; i < behavior.Operations.Count; i++)
            {
                ClientOperation operation = behavior.Operations[i];
                ProxyOperationRuntime operationRuntime = new ProxyOperationRuntime(operation, this);
                _operations.Add(operation.Name, operationRuntime);
            }

            _correlationCount = _messageInspectors.Length + behavior.MaxParameterInspectors;
        }

        internal int MessageInspectorCorrelationOffset
        {
            get { return 0; }
        }

        internal int ParameterInspectorCorrelationOffset
        {
            get { return _messageInspectors.Length; }
        }

        internal int CorrelationCount
        {
            get { return _correlationCount; }
        }

        internal IClientOperationSelector OperationSelector
        {
            get { return _operationSelector; }
        }

        internal ProxyOperationRuntime UnhandledProxyOperation
        {
            get { return _unhandled; }
        }

        internal bool UseSynchronizationContext
        {
            get { return _useSynchronizationContext; }
        }

        internal bool ValidateMustUnderstand
        {
            get { return _validateMustUnderstand; }
            set { _validateMustUnderstand = value; }
        }

        internal void AfterReceiveReply(ref ProxyRpc rpc)
        {
            int offset = this.MessageInspectorCorrelationOffset;
            try
            {
                for (int i = 0; i < _messageInspectors.Length; i++)
                {
                    _messageInspectors[i].AfterReceiveReply(ref rpc.Reply, rpc.Correlation[offset + i]);
                    if (WcfEventSource.Instance.ClientMessageInspectorAfterReceiveInvokedIsEnabled())
                    {
                        WcfEventSource.Instance.ClientMessageInspectorAfterReceiveInvoked(rpc.EventTraceActivity, _messageInspectors[i].GetType().FullName);
                    }
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                if (ErrorBehavior.ShouldRethrowClientSideExceptionAsIs(e))
                {
                    throw;
                }
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperCallback(e);
            }
        }

        internal void BeforeSendRequest(ref ProxyRpc rpc)
        {
            int offset = this.MessageInspectorCorrelationOffset;
            try
            {
                for (int i = 0; i < _messageInspectors.Length; i++)
                {
                    ServiceChannel clientChannel = ServiceChannelFactory.GetServiceChannel(rpc.Channel.Proxy);
                    rpc.Correlation[offset + i] = _messageInspectors[i].BeforeSendRequest(ref rpc.Request, clientChannel);
                    if (WcfEventSource.Instance.ClientMessageInspectorBeforeSendInvokedIsEnabled())
                    {
                        WcfEventSource.Instance.ClientMessageInspectorBeforeSendInvoked(rpc.EventTraceActivity, _messageInspectors[i].GetType().FullName);
                    }
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                if (ErrorBehavior.ShouldRethrowClientSideExceptionAsIs(e))
                {
                    throw;
                }
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperCallback(e);
            }
        }

        internal void DisplayInitializationUI(ServiceChannel channel)
        {
            EndDisplayInitializationUI(BeginDisplayInitializationUI(channel, null, null));
        }

        internal IAsyncResult BeginDisplayInitializationUI(ServiceChannel channel, AsyncCallback callback, object state)
        {
            return new DisplayInitializationUIAsyncResult(channel, _interactiveChannelInitializers, callback, state);
        }

        internal void EndDisplayInitializationUI(IAsyncResult result)
        {
            DisplayInitializationUIAsyncResult.End(result);
        }

        internal void InitializeChannel(IClientChannel channel)
        {
            try
            {
                for (int i = 0; i < _channelInitializers.Length; ++i)
                {
                    _channelInitializers[i].Initialize(channel);
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                if (ErrorBehavior.ShouldRethrowClientSideExceptionAsIs(e))
                {
                    throw;
                }
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperCallback(e);
            }
        }

        internal ProxyOperationRuntime GetOperation(MethodBase methodBase, object[] args, out bool canCacheResult)
        {
            if (_operationSelector == null)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new NotSupportedException
                                                        (SR.Format(SR.SFxNeedProxyBehaviorOperationSelector2,
                                                                      methodBase.Name,
                                                                      methodBase.DeclaringType.Name)));
            }

            try
            {
                if (_operationSelector.AreParametersRequiredForSelection)
                {
                    canCacheResult = false;
                }
                else
                {
                    args = null;
                    canCacheResult = true;
                }
                string operationName = _operationSelector.SelectOperation(methodBase, args);
                ProxyOperationRuntime operation;
                if ((operationName != null) && _operations.TryGetValue(operationName, out operation))
                {
                    return operation;
                }
                else
                {
                    // did not find the right operation, will not know how 
                    // to invoke the method.
                    return null;
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                if (ErrorBehavior.ShouldRethrowClientSideExceptionAsIs(e))
                {
                    throw;
                }
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperCallback(e);
            }
        }

        internal ProxyOperationRuntime GetOperationByName(string operationName)
        {
            ProxyOperationRuntime operation = null;
            if (_operations.TryGetValue(operationName, out operation))
                return operation;
            else
                return null;
        }

        internal class DisplayInitializationUIAsyncResult : System.Runtime.AsyncResult
        {
            private ServiceChannel _channel;
            private int _index = -1;
            private IInteractiveChannelInitializer[] _initializers;
            private IClientChannel _proxy;

            private static AsyncCallback s_callback = Fx.ThunkCallback(new AsyncCallback(DisplayInitializationUIAsyncResult.Callback));

            internal DisplayInitializationUIAsyncResult(ServiceChannel channel,
                                                        IInteractiveChannelInitializer[] initializers,
                                                        AsyncCallback callback, object state)
                : base(callback, state)
            {
                _channel = channel;
                _initializers = initializers;
                _proxy = ServiceChannelFactory.GetServiceChannel(channel.Proxy);
                this.CallBegin(true);
            }

            private void CallBegin(bool completedSynchronously)
            {
                while (++_index < _initializers.Length)
                {
                    IAsyncResult result = null;
                    Exception exception = null;

                    try
                    {
                        result = _initializers[_index].BeginDisplayInitializationUI(
                            _proxy,
                            DisplayInitializationUIAsyncResult.s_callback,
                            this
                        );
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        exception = e;
                    }

                    if (exception == null)
                    {
                        if (!result.CompletedSynchronously)
                        {
                            return;
                        }

                        this.CallEnd(result, out exception);
                    }

                    if (exception != null)
                    {
                        this.CallComplete(completedSynchronously, exception);
                        return;
                    }
                }

                this.CallComplete(completedSynchronously, null);
            }

            private static void Callback(IAsyncResult result)
            {
                if (result.CompletedSynchronously)
                {
                    return;
                }

                DisplayInitializationUIAsyncResult outer = (DisplayInitializationUIAsyncResult)result.AsyncState;
                Exception exception = null;

                outer.CallEnd(result, out exception);

                if (exception != null)
                {
                    outer.CallComplete(false, exception);
                    return;
                }

                outer.CallBegin(false);
            }

            private void CallEnd(IAsyncResult result, out Exception exception)
            {
                try
                {
                    _initializers[_index].EndDisplayInitializationUI(result);
                    exception = null;
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    exception = e;
                }
            }

            private void CallComplete(bool completedSynchronously, Exception exception)
            {
                this.Complete(completedSynchronously, exception);
            }

            internal static void End(IAsyncResult result)
            {
                System.Runtime.AsyncResult.End<DisplayInitializationUIAsyncResult>(result);
            }
        }
    }
}
