namespace ProjectT.Pool
{
    public interface IPoolable
    {
        void OnReturn();
        void OnGet();
    }
}