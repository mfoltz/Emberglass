namespace Emberglass.Network;
internal sealed class NetBuffer(int totalParts)
{
    readonly int _totalParts = totalParts;
    readonly string[] _parts = new string[totalParts];
    int _received;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool AddPart(int index, string fragment)
    {
        if (_parts[index] is not null)
        {
            return false;
        }

        _parts[index] = fragment;
        _received++;
        LastSeen = DateTime.UtcNow;

        return _received == _totalParts;
    }
    public string Concat() => string.Concat(_parts);
}
