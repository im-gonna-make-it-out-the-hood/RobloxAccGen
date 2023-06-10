namespace AccountGenerator; 

public struct ProxyResult {
    public string host;
    public ushort port;
    public bool isSuccess;

    public ProxyResult(string host, ushort port, bool isSuccess) {
        this.host = host;
        this.port = port;
        this.isSuccess = isSuccess;
    }
}