using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113MobNodeRequest(int MobObjectId, int NodeIndex);

internal readonly record struct V113MobNodeResult(V113MobNodeRequest Request, bool MobFound);

internal static class V113MobNodeHandler
{
    public static V113MobNodeRequest Parse(PacketReader reader)
    {
        var mobObjectId = reader.ReadInt();
        var nodeIndex = reader.ReadInt();
        return new V113MobNodeRequest(mobObjectId, nodeIndex);
    }

    public static V113MobNodeResult Handle(PacketReader reader, FieldInstance field)
    {
        var request = Parse(reader);
        bool found;
        lock (field)
        {
            found = field.GetMob(request.MobObjectId) is not null;
        }

        return new V113MobNodeResult(request, found);
    }
}
