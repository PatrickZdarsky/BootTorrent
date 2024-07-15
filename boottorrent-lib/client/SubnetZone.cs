using System.Collections;

namespace boottorrent_lib.client;

public class SubnetZone(string subnet) : IZone
{
    public IEnumerator<Machine> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}