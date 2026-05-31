using ControlPlane.Features.AgentTokens;
using ControlPlane.Infrastructure;
using NSubstitute;
using Shared.Domain;

namespace ControlPlane.Tests.Features.AgentTokens;

public class RecordExecutionLogHandlerTests
{
    private readonly IExecutionLogRepository _repository = Substitute.For<IExecutionLogRepository>();
    private readonly RecordExecutionLogHandler _handler;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _executionId = Guid.NewGuid();
    private const string Environment = "production";

    public RecordExecutionLogHandlerTests()
    {
        _handler = new RecordExecutionLogHandler(_repository);
        _repository.CreateAsync(Arg.Any<ExecutionLog>()).Returns(call => call.Arg<ExecutionLog>());
    }

    [Fact]
    public async Task HandleAsync_ValidLog_CreatesExecutionLog()
    {
        var timestamp = DateTime.UtcNow;
        _repository.ExecutionExistsAsync(_tenantId, Environment, _executionId).Returns(true);

        await _handler.HandleAsync(new RecordExecutionLogCommand(
            _tenantId,
            Environment,
            _executionId,
            timestamp,
            "Information",
            "Synced 5 orders",
            null,
            """{"Count":"5"}"""));

        await _repository.Received(1).CreateAsync(Arg.Is<ExecutionLog>(log =>
            log.TenantId == _tenantId &&
            log.ExecutionRecordId == _executionId &&
            log.Timestamp == timestamp &&
            log.Level == "Information" &&
            log.Message == "Synced 5 orders" &&
            log.PropertiesJson == """{"Count":"5"}"""));
    }

    [Fact]
    public async Task HandleAsync_ExecutionNotFound_ThrowsNotFoundException()
    {
        _repository.ExecutionExistsAsync(_tenantId, Environment, _executionId).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new RecordExecutionLogCommand(
                _tenantId,
                Environment,
                _executionId,
                DateTime.UtcNow,
                "Information",
                "Message",
                null,
                null)));
    }

    [Theory]
    [InlineData("", "Message", "Log level is required.")]
    [InlineData("Information", "", "Log message is required.")]
    public async Task HandleAsync_InvalidInput_ThrowsValidationException(
        string level,
        string message,
        string expectedMessage)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new RecordExecutionLogCommand(
                _tenantId,
                Environment,
                _executionId,
                DateTime.UtcNow,
                level,
                message,
                null,
                null)));

        Assert.Equal(expectedMessage, ex.Message);
    }
}
