namespace Assets.Scripts.MapGenerator
{
    public interface IGenerator
    {
        public void Generate(int seed);

        void Clear();
    }
}
