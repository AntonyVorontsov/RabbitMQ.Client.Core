using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Core.DependencyInjection.Configuration;
using RabbitMQ.Client.Core.DependencyInjection.Models;
using RabbitMQ.Client.Core.DependencyInjection.Services.Interfaces;
using RabbitMQ.Client.Events;

namespace RabbitMQ.Client.Core.DependencyInjection.Services
{
    /// <summary>
    /// Hosted service that is responsible for creating connections and channels for both producing and consuming services.
    /// </summary>
    public class ChannelDeclarationHostedService : IHostedService
    {
        readonly RabbitMqConnectionOptionsContainer _producerOptions;
        readonly RabbitMqConnectionOptionsContainer _consumerOptions;
        private readonly IProducingService _producingService;
        private readonly IConsumingService _consumingService;
        readonly IRabbitMqConnectionFactory _rabbitMqConnectionFactory;
        readonly IEnumerable<RabbitMqExchange> _exchanges;
        readonly ILogger<ChannelDeclarationHostedService> _logger;
        
        public ChannelDeclarationHostedService(
            IProducingService _producingService,
            IConsumingService _consumingService,
            IRabbitMqConnectionFactory rabbitMqConnectionFactory,
            IEnumerable<RabbitMqConnectionOptionsContainer> connectionOptionsContainers,
            IEnumerable<RabbitMqExchange> exchanges,
            ILogger<ChannelDeclarationHostedService> logger)
        {
            this._producingService = _producingService;
            this._consumingService = _consumingService;
            _rabbitMqConnectionFactory = rabbitMqConnectionFactory;
            var options = connectionOptionsContainers.ToList();
            _producerOptions = options.FirstOrDefault(x => x.Type == typeof(IProducingService));
            _consumerOptions = options.FirstOrDefault(x => x.Type == typeof(IConsumingService));
            _exchanges = exchanges;
            _logger = logger;
        }
        
        // TODO: Add a channel + connection container?
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_producerOptions != null)
            {
                var connection = CreateConnection(_producerOptions);
                var channel = CreateChannel(connection);
                StartClient(channel);
                _producingService.UseConnection(connection);
                _producingService.UseChannel(channel);
            }

            if (_consumerOptions != null)
            {
                var connection = CreateConnection(_consumerOptions);
                var channel = CreateChannel(connection);
                StartClient(channel);
                var consumer = _rabbitMqConnectionFactory.CreateConsumer(channel);
                _consumingService.UseConnection(connection);
                _consumingService.UseChannel(channel);
                _consumingService.UseConsumer(consumer);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        IConnection CreateConnection(RabbitMqConnectionOptionsContainer optionsContainer) => _rabbitMqConnectionFactory.CreateRabbitMqConnection(optionsContainer.Options?.ConsumerOptions);

        IModel CreateChannel(IConnection connection)
        {
            connection.CallbackException += HandleConnectionCallbackException;
            if (connection is IAutorecoveringConnection recoveringConnection)
            {
                recoveringConnection.ConnectionRecoveryError += HandleConnectionRecoveryError;
            }
            
            var channel = connection.CreateModel();
            channel.CallbackException += HandleChannelCallbackException;
            channel.BasicRecoverOk += HandleChannelBasicRecoverOk;
            return channel;
        }

        void StartClient(IModel channel)
        {
            var deadLetterExchanges = _exchanges
                .Where(x => !string.IsNullOrEmpty(x.Options?.DeadLetterExchange))
                .Select(x => x.Options.DeadLetterExchange)
                .Distinct()
                .ToList();

            StartChannel(channel, _exchanges, deadLetterExchanges);
        }
        
        static void StartChannel(IModel channel, IEnumerable<RabbitMqExchange> exchanges, IEnumerable<string> deadLetterExchanges)
        {
            if (channel is null)
            {
                return;
            }

            foreach (var exchangeName in deadLetterExchanges)
            {
                StartDeadLetterExchange(channel, exchangeName);
            }

            foreach (var exchange in exchanges)
            {
                StartExchange(channel, exchange);
            }
        }

        static void StartDeadLetterExchange(IModel channel, string exchangeName)
        {
            channel.ExchangeDeclare(
                exchange: exchangeName,
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null);
        }

        static void StartExchange(IModel channel, RabbitMqExchange exchange)
        {
            channel.ExchangeDeclare(
                exchange: exchange.Name,
                type: exchange.Options.Type,
                durable: exchange.Options.Durable,
                autoDelete: exchange.Options.AutoDelete,
                arguments: exchange.Options.Arguments);

            foreach (var queue in exchange.Options.Queues)
            {
                StartQueue(channel, queue, exchange.Name);
            }
        }

        static void StartQueue(IModel channel, RabbitMqQueueOptions queue, string exchangeName)
        {
            channel.QueueDeclare(
                queue: queue.Name,
                durable: queue.Durable,
                exclusive: queue.Exclusive,
                autoDelete: queue.AutoDelete,
                arguments: queue.Arguments);

            if (queue.RoutingKeys.Count > 0)
            {
                foreach (var route in queue.RoutingKeys)
                {
                    channel.QueueBind(
                        queue: queue.Name,
                        exchange: exchangeName,
                        routingKey: route);
                }
            }
            else
            {
                // If there are not any routing keys then make a bind with a queue name.
                channel.QueueBind(
                    queue: queue.Name,
                    exchange: exchangeName,
                    routingKey: queue.Name);
            }
        }
        
        void HandleConnectionCallbackException(object sender, CallbackExceptionEventArgs @event)
        {
            if (@event is null)
            {
                return;
            }

            _logger.LogError(new EventId(), @event.Exception, @event.Exception.Message, @event);
            throw @event.Exception;
        }

        void HandleConnectionRecoveryError(object sender, ConnectionRecoveryErrorEventArgs @event)
        {
            if (@event is null)
            {
                return;
            }

            _logger.LogError(new EventId(), @event.Exception, @event.Exception.Message, @event);
            throw @event.Exception;
        }

        void HandleChannelBasicRecoverOk(object sender, EventArgs @event)
        {
            if (@event is null)
            {
                return;
            }
            
            _logger.LogInformation("Connection has been reestablished");
        }

        void HandleChannelCallbackException(object sender, CallbackExceptionEventArgs @event)
        {
            if (@event is null)
            {
                return;
            }

            _logger.LogError(new EventId(), @event.Exception, @event.Exception.Message, @event);
        }
    }
}