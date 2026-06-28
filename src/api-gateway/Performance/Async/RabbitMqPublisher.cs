using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace GuardrailApi.Performance.Async;

public record AsyncDetectionRequest(
    string RequestId,
    string Content,
    string ResourceType,
    string Source,
    string[] Frameworks,
    string? CallbackUrl,
    DateTime SubmittedAt);

public record AsyncDetectionResponse(
    string RequestId,
    string Status,    // queued, processing, completed, failed
    string? Decision,
    double? Confidence,
    int? ViolationCount,
    string? CallbackUrl,
    DateTime SubmittedAt,
    DateTime? CompletedAt);

public class RabbitMqPublisher : IDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private const string ExchangeName = "guardrail";
    private const string LlmQueueName = "guardrail.llm-judge";
    private const string AuditQueueName = "guardrail.audit-log";
    private const string RemediationQueueName = "guardrail.remediation";

    public static async Task<RabbitMqPublisher> CreateAsync(string host = "localhost", string user = "guardrail", string password = "guardrail_dev")
    {
        var factory = new ConnectionFactory
        {
            HostName = host,
            UserName = user,
            Password = password,
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);

        await channel.QueueDeclareAsync(LlmQueueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(LlmQueueName, ExchangeName, "llm.judge");

        await channel.QueueDeclareAsync(AuditQueueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(AuditQueueName, ExchangeName, "audit.log");

        await channel.QueueDeclareAsync(RemediationQueueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(RemediationQueueName, ExchangeName, "remediation.*");

        return new RabbitMqPublisher(connection, channel);
    }

    private RabbitMqPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public async Task PublishLlmJudgeRequestAsync(AsyncDetectionRequest request)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request);
        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = request.RequestId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            ContentType = "application/json",
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: "llm.judge",
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    public async Task PublishAuditLogAsync<T>(T auditEntry)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(auditEntry);
        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: "audit.log",
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    public async Task PublishRemediationAsync(string requestId, string action, string content)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { requestId, action, content });
        var props = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: $"remediation.{action}",
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    public async Task<QueueStats> GetQueueStatsAsync()
    {
        var llm = await _channel.QueueDeclarePassiveAsync(LlmQueueName);
        var audit = await _channel.QueueDeclarePassiveAsync(AuditQueueName);
        var remediation = await _channel.QueueDeclarePassiveAsync(RemediationQueueName);

        return new QueueStats(
            LlmJudgeDepth: llm.MessageCount,
            AuditLogDepth: audit.MessageCount,
            RemediationDepth: remediation.MessageCount);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}

public record QueueStats(uint LlmJudgeDepth, uint AuditLogDepth, uint RemediationDepth);
