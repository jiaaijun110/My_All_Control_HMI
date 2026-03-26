namespace Common.CPlc
{
    public interface IPlc
    {
 
        Task<bool> ConnectAsync();
        Task<byte[]?> ReadBytesAsync(int db, int startByte, int count);
        Task WriteAsync(string address, object value);
        bool IsConnected { get; }
        void ReadClass(object instance, int db, int startByte = 0);
    }
}