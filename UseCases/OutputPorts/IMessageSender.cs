namespace UseCases.OutputPorts;

public interface IMessageSender
{
    Task SendMessageAsync(string message, string channelId);
}