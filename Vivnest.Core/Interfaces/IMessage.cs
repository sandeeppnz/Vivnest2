namespace Vivnest.Core.Interfaces;

public interface IMessage
{
    Task PublishAsync<T>(T message);

}