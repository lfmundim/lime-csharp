using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Lime.Protocol.Server;

namespace Lime.Protocol.Network
{
    internal sealed class ReceiverChannel : IReceiverChannel, IStoppable, IDisposable
    {
        private readonly IChannelInformation _channelInformation;
        private readonly ITransport _transport;
        private readonly IChannelCommandProcessor _channelCommandProcessor;
        private readonly ICollection<IChannelModule<Message>> _messageModules;
        private readonly ICollection<IChannelModule<Notification>> _notificationModules;
        private readonly ICollection<IChannelModule<Command>> _commandModules;

        private readonly TimeSpan? _consumeTimeout;
        private readonly CancellationTokenSource _consumerCts;
        private readonly BufferBlock<Envelope> _receiveEnvelopeBuffer;
        private readonly TransformBlock<Envelope, Message> _messageConsumerBlock;
        private readonly TransformBlock<Envelope, Command> _commandConsumerBlock;
        private readonly TransformBlock<Envelope, Notification> _notificationConsumerBlock;
        private readonly TransformBlock<Envelope, Session> _sessionConsumerBlock;
        private readonly BufferBlock<Message> _receiveMessageBuffer;
        private readonly BufferBlock<Command> _receiveCommandBuffer;
        private readonly BufferBlock<Notification> _receiveNotificationBuffer;
        private readonly BufferBlock<Session> _receiveSessionBuffer;
        private readonly ITargetBlock<Envelope> _drainEnvelopeBlock;
        private readonly SemaphoreSlim _sessionSemaphore;
        private readonly SemaphoreSlim _startStopSemaphore;
        private readonly ActionBlock<Exception> _exceptionHandlerActionBlock;
        
        private Task _consumeTransportTask;
        private bool _isDisposing;

        public ReceiverChannel(
            IChannelInformation channelInformation,
            ITransport transport,
            IChannelCommandProcessor channelCommandProcessor,
            ICollection<IChannelModule<Message>> messageModules,
            ICollection<IChannelModule<Notification>> notificationModules,
            ICollection<IChannelModule<Command>> commandModules,
            Func<Exception, Task> exceptionHandler,
            int envelopeBufferSize,
            TimeSpan? consumeTimeout)
        {
            if (consumeTimeout != null && consumeTimeout.Value == default) throw new ArgumentException("Invalid consume timeout", nameof(consumeTimeout));
            
            _channelInformation = channelInformation;
            _transport = transport;
            _channelCommandProcessor = channelCommandProcessor;
            _messageModules = messageModules;
            _notificationModules = notificationModules;
            _commandModules = commandModules;
            _consumeTimeout = consumeTimeout;
            _exceptionHandlerActionBlock = new ActionBlock<Exception>(exceptionHandler, DataflowUtils.UnboundedUnorderedExecutionDataflowBlockOptions);
            _sessionSemaphore = new SemaphoreSlim(1);
            _startStopSemaphore = new SemaphoreSlim(1);
            
            // Receive pipeline
            _consumerCts = new CancellationTokenSource();
            var consumerDataflowBlockOptions = new ExecutionDataflowBlockOptions()
            {
                BoundedCapacity = envelopeBufferSize,
                MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded,
                EnsureOrdered = false
            };
            _receiveEnvelopeBuffer = new BufferBlock<Envelope>(consumerDataflowBlockOptions);        
            _messageConsumerBlock = new TransformBlock<Envelope, Message>(e => ConsumeMessageAsync(e), consumerDataflowBlockOptions);
            _commandConsumerBlock = new TransformBlock<Envelope, Command>(e => ConsumeCommandAsync(e), consumerDataflowBlockOptions);
            _notificationConsumerBlock = new TransformBlock<Envelope, Notification>(e => ConsumeNotificationAsync(e), consumerDataflowBlockOptions);
            _sessionConsumerBlock = new TransformBlock<Envelope, Session>(e => ConsumeSession(e), consumerDataflowBlockOptions);
            _receiveMessageBuffer = new BufferBlock<Message>(consumerDataflowBlockOptions);
            _receiveCommandBuffer = new BufferBlock<Command>(consumerDataflowBlockOptions);
            _receiveNotificationBuffer = new BufferBlock<Notification>(consumerDataflowBlockOptions);
            _receiveSessionBuffer = new BufferBlock<Session>(consumerDataflowBlockOptions);
            _drainEnvelopeBlock = DataflowBlock.NullTarget<Envelope>();
            _receiveEnvelopeBuffer.LinkTo(_messageConsumerBlock, DataflowUtils.PropagateCompletionLinkOptions, e => e is Message);
            _receiveEnvelopeBuffer.LinkTo(_commandConsumerBlock, DataflowUtils.PropagateCompletionLinkOptions, e => e is Command);
            _receiveEnvelopeBuffer.LinkTo(_notificationConsumerBlock, DataflowUtils.PropagateCompletionLinkOptions, e => e is Notification);
            _receiveEnvelopeBuffer.LinkTo(_sessionConsumerBlock, DataflowUtils.PropagateCompletionLinkOptions, e => e is Session);
            _messageConsumerBlock.LinkTo(_receiveMessageBuffer, DataflowUtils.PropagateCompletionLinkOptions, e => e != null);
            _messageConsumerBlock.LinkTo(_drainEnvelopeBlock, e => e == null);
            _commandConsumerBlock.LinkTo(_receiveCommandBuffer, DataflowUtils.PropagateCompletionLinkOptions, e => e != null);
            _commandConsumerBlock.LinkTo(_drainEnvelopeBlock, e => e == null);
            _notificationConsumerBlock.LinkTo(_receiveNotificationBuffer, DataflowUtils.PropagateCompletionLinkOptions, e => e != null);
            _notificationConsumerBlock.LinkTo(_drainEnvelopeBlock, e => e == null);
            _sessionConsumerBlock.LinkTo(_receiveSessionBuffer, DataflowUtils.PropagateCompletionLinkOptions, e => e != null);
            _sessionConsumerBlock.LinkTo(_drainEnvelopeBlock, e => e == null);
        }
        
        /// <inheritdoc />
        public Task<Message> ReceiveMessageAsync(CancellationToken cancellationToken)
            => ReceiveFromBufferAsync(_receiveMessageBuffer, cancellationToken);
        
        /// <inheritdoc />
        public Task<Command> ReceiveCommandAsync(CancellationToken cancellationToken)
            => ReceiveFromBufferAsync(_receiveCommandBuffer, cancellationToken);
        
        /// <inheritdoc />
        public Task<Notification> ReceiveNotificationAsync(CancellationToken cancellationToken)
            => ReceiveFromBufferAsync(_receiveNotificationBuffer, cancellationToken);
        
        public async Task<Session> ReceiveSessionAsync(CancellationToken cancellationToken)
        {
            switch (_channelInformation.State)
            {
                case SessionState.Finished:
                    throw new InvalidOperationException(
                        $"Cannot receive a session in the '{_channelInformation.State}' session state");

                case SessionState.Established:
                    return await ReceiveFromBufferAsync(_receiveSessionBuffer, cancellationToken)
                        .ConfigureAwait(false);
            }

            await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // The session envelopes are received directly from the transport, except when the session is established
                var envelope = await ReceiveFromTransportAsync(cancellationToken).ConfigureAwait(false);
                if (envelope is Session session) return session;
                throw new InvalidOperationException("An empty or unexpected envelope was received from the transport");
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }
        
        public void Start()
        {
            _startStopSemaphore.Wait();
            try
            {
                if (_consumeTransportTask == null)
                {
                    _consumeTransportTask = Task.Run(ConsumeTransportAsync);
                }
            }
            finally
            {
                _startStopSemaphore.Release();
            }
        }
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _startStopSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Complete the pipeline
                _receiveEnvelopeBuffer.CompleteIfNotCompleted();
                
                // Stops the listener task
                _consumerCts.CancelIfNotRequested();
                if (_consumeTransportTask != null &&
                    !_consumeTransportTask.IsCompleted)
                {
                    await _consumeTransportTask.WithCancellation(cancellationToken);
                }
            }
            finally
            {
                _startStopSemaphore.Release();
            }
        }
        
        public void Dispose()
        {
            _isDisposing = true;
            _consumerCts.CancelIfNotRequested();
            _consumerCts.Dispose();
            _sessionSemaphore.Dispose();
            _startStopSemaphore.Dispose();
        }

        private bool IsChannelEstablished()
            => !_consumerCts.IsCancellationRequested &&
                _channelInformation.State == SessionState.Established &&
                _transport.IsConnected;

        private async Task ConsumeTransportAsync()
        {
            try
            {
                while (IsChannelEstablished())
                {
                    try
                    {
                        var envelope = await ReceiveFromTransportAsync(_consumerCts.Token).ConfigureAwait(false);
                        if (envelope == null) continue;

                        using var timeoutCts = _consumeTimeout == null ? new CancellationTokenSource() : new CancellationTokenSource(_consumeTimeout.Value);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _consumerCts.Token);
                        
                        try
                        {
                            if (!await _receiveEnvelopeBuffer.SendAsync(envelope, linkedCts.Token).ConfigureAwait(false))
                            {
                                // The buffer is complete
                                break;
                            }
                        }
                        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && _consumeTimeout != null)
                        {
                            var exceptionMessageBuilder = new StringBuilder($"The transport consumer has timed out after {_consumeTimeout.Value.TotalSeconds} seconds.");
                            if (_receiveMessageBuffer.Count > 0
                                || _receiveNotificationBuffer.Count > 0
                                || _receiveCommandBuffer.Count > 0
                                || _receiveSessionBuffer.Count > 0)
                            {
                                exceptionMessageBuilder.Append(
                                    $" The receiver buffer has {_receiveMessageBuffer.Count} ({_messageConsumerBlock.InputCount}/{_messageConsumerBlock.OutputCount}) messages,");
                                exceptionMessageBuilder.Append(
                                    $" {_receiveNotificationBuffer.Count} ({_notificationConsumerBlock.InputCount}/{_notificationConsumerBlock.OutputCount}) notifications,");
                                exceptionMessageBuilder.Append(
                                    $" {_receiveCommandBuffer.Count} ({_commandConsumerBlock.InputCount}/{_commandConsumerBlock.OutputCount}) commands,");
                                exceptionMessageBuilder.Append(
                                    $" and {_receiveSessionBuffer.Count} sessions and it may be the cause of the problem. Please ensure that the channel receive methods are being called.");
                            }

                            throw new TimeoutException(exceptionMessageBuilder.ToString(), ex);
                        }
                    }
                    catch (OperationCanceledException) when (_consumerCts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (_isDisposing)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await RaiseConsumerExceptionAsync(ex);
                        break;
                    }
                }
            }
            finally
            {
                // Complete the receive pipeline to propagate to the envelope specific buffers
                _receiveEnvelopeBuffer.CompleteIfNotCompleted();
                _channelCommandProcessor.CancelAll();
                _consumerCts.CancelIfNotRequested();
            }
        }

        private Task<Message> ConsumeMessageAsync(Envelope envelope) => OnReceivingAsync((Message)envelope, _messageModules, _consumerCts.Token);

        private async Task<Command> ConsumeCommandAsync(Envelope envelope)
        {
            var command = await OnReceivingAsync((Command)envelope, _commandModules, _consumerCts.Token);;

            try
            {
                if (command != null &&
                    !_channelCommandProcessor.TrySubmitCommandResult(command))
                {
                    return command;
                }
            }
            catch (OperationCanceledException) when (_consumerCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await RaiseConsumerExceptionAsync(ex);
                _consumerCts.CancelIfNotRequested();
            }
            return null;
        }

        private Task<Notification> ConsumeNotificationAsync(Envelope envelope) => OnReceivingAsync((Notification)envelope, _notificationModules, _consumerCts.Token);

        private Session ConsumeSession(Envelope envelope) => (Session) envelope;

        private async Task<T> OnReceivingAsync<T>(T envelope, IEnumerable<IChannelModule<T>> modules, CancellationToken cancellationToken) 
            where T : Envelope, new()
        {
            try
            {
                foreach (var module in modules.ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (envelope == null) break;
                    envelope = await module.OnReceivingAsync(envelope, cancellationToken);
                }

                return envelope;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await RaiseConsumerExceptionAsync(ex);
            }

            return null;
        }
        
        /// <summary>
        /// Receives an envelope directly from the transport.
        /// </summary>
        private Task<Envelope> ReceiveFromTransportAsync(CancellationToken cancellationToken) => _transport.ReceiveAsync(cancellationToken);

        /// <summary>
        /// Receives an envelope from the buffer.
        /// </summary>
        private async Task<T> ReceiveFromBufferAsync<T>(ISourceBlock<T> buffer, CancellationToken cancellationToken) 
            where T : Envelope, new()
        {
            if (_channelInformation.State < SessionState.Established)
            {
                throw new InvalidOperationException($"Cannot receive envelopes in the '{_channelInformation.State}' session state");
            }

            try
            {
                return await buffer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (buffer.Completion.IsCompleted)
            {
                throw new InvalidOperationException("The channel listener task is complete and cannot receive envelopes", ex);
            }
        }
        
        /// <summary>
        /// Asynchronously raises the channel exception to avoid deadlocks issues.
        /// </summary>
        private Task RaiseConsumerExceptionAsync(Exception exception) => _exceptionHandlerActionBlock.SendAsync(exception);
    }
}