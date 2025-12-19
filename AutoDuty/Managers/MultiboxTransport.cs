namespace AutoDuty.Managers
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public enum TransportType
    {
        NamedPipe = 0,
        Tcp = 1
    }

    public interface ITransport : IDisposable
    {
        void StartServer(int backlog = 3);
        void StopServer();
        Task<Stream> AcceptConnectionAsync(CancellationToken ct);
        Task<Stream> ConnectToServerAsync(CancellationToken ct);
    }
}
