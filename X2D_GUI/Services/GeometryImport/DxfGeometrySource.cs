using netDxf;
using netDxf.Entities;

namespace GeotechSlopeApp.Services.GeometryImport;

internal static class DxfGeometrySource
{
  public static bool IsSystemBlockName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        if (name.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase))
            return true;

        // Paper-space layout blocks only (*Paper_Space, *Paper_Space0, ...).
        return name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<EntityObject> CollectLayoutEntities(DxfDocument dxf)
    {
        var seen = new HashSet<EntityObject>();
        foreach (var layout in dxf.Layouts)
        {
            foreach (var entity in layout.AssociatedBlock.Entities)
            {
                if (IsNonImportableLayoutEntity(entity))
                    continue;

                if (seen.Add(entity))
                    yield return entity;
            }
        }
    }

    public static IEnumerable<EntityObject> CollectBlockDefinitionEntities(DxfDocument dxf)
    {
        var seen = new HashSet<EntityObject>();
        foreach (var block in dxf.Blocks)
        {
            if (IsSystemBlockName(block.Name))
                continue;

            foreach (var entity in block.Entities)
            {
                if (seen.Add(entity))
                    yield return entity;
            }
        }
    }

    public static bool IsNonImportableLayoutEntity(EntityObject entity) =>
        entity is Viewport or Image or Underlay;

    public static IEnumerable<EntityObject> FlattenEntities(IEnumerable<EntityObject> roots, int maxInsertDepth = 8)
    {
        foreach (var root in roots)
        {
            foreach (var flat in FlattenEntity(root, 0, maxInsertDepth))
                yield return flat;
        }
    }

    private static IEnumerable<EntityObject> FlattenEntity(EntityObject entity, int depth, int maxInsertDepth)
    {
        if (IsNonImportableLayoutEntity(entity))
            yield break;

        if (entity is Insert insert && depth < maxInsertDepth)
        {
            IReadOnlyList<EntityObject> exploded;
            try
            {
                exploded = insert.Explode();
            }
            catch
            {
                exploded = Array.Empty<EntityObject>();
            }

            if (exploded.Count > 0)
            {
                foreach (var child in exploded)
                {
                    foreach (var flat in FlattenEntity(child, depth + 1, maxInsertDepth))
                        yield return flat;
                }

                yield break;
            }

            if (insert.Block != null)
            {
                foreach (var child in insert.Block.Entities)
                {
                    foreach (var flat in FlattenEntity(child, depth + 1, maxInsertDepth))
                        yield return flat;
                }
            }

            yield break;
        }

        if (entity is MLine mLine)
        {
            foreach (var child in mLine.Explode())
            {
                foreach (var flat in FlattenEntity(child, depth, maxInsertDepth))
                    yield return flat;
            }

            yield break;
        }

        yield return entity;
    }
}
