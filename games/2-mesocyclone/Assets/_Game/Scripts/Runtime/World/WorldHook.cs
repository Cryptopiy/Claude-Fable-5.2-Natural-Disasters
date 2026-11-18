namespace MESOCYCLONE
{
    /// <summary>Everything man-made on the land (poles, wind farm, town, farms, signs, cars, gas station, debris).</summary>
    public static partial class Extras
    {
        static partial void BuildWorld(Bootstrap boot)
        {
            WorldProps.Build(Bootstrap.WorldRoot);
            GrassField.Build(boot.transform);
        }
    }
}
