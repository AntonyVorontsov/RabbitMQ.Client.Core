using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client.Core.DependencyInjection.Configuration;
using RabbitMQ.Client.Core.DependencyInjection.Models;
using RabbitMQ.Client.Core.DependencyInjection.Services;
using RabbitMQ.Client.Core.DependencyInjection.Services.Interfaces;
using RabbitMQ.Client.Events;
using Xunit;

namespace RabbitMQ.Client.Core.DependencyInjection.Tests.UnitTests
{
    public class QueueServiceConsumerTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(25)]
        public async Task ShouldProperlyConsumeMessages(int numberOfMessages)
        {
            var channelMock = new Mock<IModel>();
            var connectionMock = new Mock<IConnection>();
            connectionMock.Setup(x => x.CreateModel())
                .Returns(channelMock.Object);

            var connectionFactoryMock = new Mock<IRabbitMqConnectionFactory>();
            connectionFactoryMock.Setup(x => x.CreateRabbitMqConnection(It.IsAny<RabbitMqClientOptions>()))
                .Returns(connectionMock.Object);

            var consumer = new AsyncEventingBasicConsumer(channelMock.Object);
            connectionFactoryMock.Setup(x => x.CreateConsumer(It.IsAny<IModel>()))
                .Returns(consumer);

            var messageHandlingPipelineExecutingServiceMock = new Mock<IMessageHandlingPipelineExecutingService>();
            var queueService = CreateService(connectionFactoryMock.Object, messageHandlingPipelineExecutingServiceMock.Object);

            await consumer.HandleBasicDeliver(
                "1",
                0,
                false,
                "exchange",
                "routing,key",
                null,
                new ReadOnlyMemory<byte>());
            messageHandlingPipelineExecutingServiceMock.Verify(x => x.Execute(It.IsAny<BasicDeliverEventArgs>(), It.IsAny<IQueueService>()), Times.Never);

            queueService.StartConsuming();

            for (var i = 1; i <= numberOfMessages; i++)
            {
                await consumer.HandleBasicDeliver(
                    "1",
                    (ulong)numberOfMessages,
                    false,
                    "exchange",
                    "routing,key",
                    null,
                    new ReadOnlyMemory<byte>());
            }

            messageHandlingPipelineExecutingServiceMock.Verify(x => x.Execute(It.IsAny<BasicDeliverEventArgs>(), It.IsAny<IQueueService>()), Times.Exactly(numberOfMessages));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(25)]
        public async Task ShouldProperlyStopConsumingMessages(int numberOfMessages)
        {
            var channelMock = new Mock<IModel>();
            var connectionMock = new Mock<IConnection>();
            connectionMock.Setup(x => x.CreateModel())
                .Returns(channelMock.Object);

            var connectionFactoryMock = new Mock<IRabbitMqConnectionFactory>();
            connectionFactoryMock.Setup(x => x.CreateRabbitMqConnection(It.IsAny<RabbitMqClientOptions>()))
                .Returns(connectionMock.Object);

            var consumer = new AsyncEventingBasicConsumer(channelMock.Object);
            connectionFactoryMock.Setup(x => x.CreateConsumer(It.IsAny<IModel>()))
                .Returns(consumer);

            var messageHandlingPipelineExecutingServiceMock = new Mock<IMessageHandlingPipelineExecutingService>();
            var queueService = CreateService(connectionFactoryMock.Object, messageHandlingPipelineExecutingServiceMock.Object);
            queueService.StartConsuming();
            for (var i = 1; i <= numberOfMessages; i++)
            {
                await consumer.HandleBasicDeliver(
                    "1",
                    (ulong)numberOfMessages,
                    false,
                    "exchange",
                    "routing,key",
                    null,
                    new ReadOnlyMemory<byte>());
            }

            messageHandlingPipelineExecutingServiceMock.Verify(x => x.Execute(It.IsAny<BasicDeliverEventArgs>(), It.IsAny<IQueueService>()), Times.Exactly(numberOfMessages));

            queueService.StopConsuming();
            await consumer.HandleBasicDeliver(
                "1",
                0,
                false,
                "exchange",
                "routing,key",
                null,
                new ReadOnlyMemory<byte>());

            messageHandlingPipelineExecutingServiceMock.Verify(x => x.Execute(It.IsAny<BasicDeliverEventArgs>(), It.IsAny<IQueueService>()), Times.Exactly(numberOfMessages));
        }

        static IConsumingService CreateService(
            IRabbitMqConnectionFactory connectionFactory,
            IMessageHandlingPipelineExecutingService messageHandlingPipelineExecutingService)
        {
            var guid = Guid.NewGuid();
            var connectionOptionContainer = new RabbitMqConnectionOptionsContainer
            {
                Guid = guid,
                Options = new RabbitMqConnectionOptions
                {
                    ConsumerOptions = new RabbitMqClientOptions()
                }
            };
            var loggerMock = new Mock<ILogger<ProducingService>>();
            return new ProducingService(
                guid,
                connectionFactory,
                new List<RabbitMqConnectionOptionsContainer> { connectionOptionContainer },
                messageHandlingPipelineExecutingService,
                new List<RabbitMqExchange>(),
                loggerMock.Object);
        }
    }
}