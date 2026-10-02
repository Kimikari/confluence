namespace Confluence.Arena.Modules
{
    /// <summary>
    /// Authored geometry of one catalogue module. Each subclass reads its record's footprint, height, materials, and
    /// durability text through the <see cref="ModuleBuilder"/> and lays out solids, ramps, patches, and sockets that
    /// realize the record's geometry sentence.
    /// </summary>
    public abstract class ModuleBlueprint
    {
        public abstract string ModuleId { get; }

        protected internal abstract void Build(ModuleBuilder b);
    }
}
