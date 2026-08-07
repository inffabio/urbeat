using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.UnitTests.Domain;

public sealed class OrderStatusStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.Received)]
    [InlineData(OrderStatus.Created, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Received)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Received, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Received, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Ready)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Ready, OrderStatus.OnDelivery)]
    [InlineData(OrderStatus.Ready, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Ready, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OnDelivery, OrderStatus.Delivered)]
    [InlineData(OrderStatus.OnDelivery, OrderStatus.Cancelled)]
    public void CanTransition_ShouldAllowExpectedTransitions(OrderStatus current, OrderStatus next)
    {
        OrderStatusStateMachine.CanTransition(current, next).Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Received, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Preparing, OrderStatus.OnDelivery)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Received)]
    public void CanTransition_ShouldRejectInvalidTransitions(OrderStatus current, OrderStatus next)
    {
        OrderStatusStateMachine.CanTransition(current, next).Should().BeFalse();
    }

    [Fact]
    public void GetNextStatuses_ShouldReturnEmptyListForTerminalStatuses()
    {
        OrderStatusStateMachine.GetNextStatuses(OrderStatus.Delivered).Should().BeEmpty();
        OrderStatusStateMachine.GetNextStatuses(OrderStatus.Cancelled).Should().BeEmpty();
    }
}
