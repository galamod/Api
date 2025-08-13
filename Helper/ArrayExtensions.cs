namespace Api.Helper
{
    public static class ArrayExtensions
    {
        public static T ElementAtOrDefault<T>(this T[] array, int index, T defaultValue = default!)
        {
            if (array == null || index < 0 || index >= array.Length)
                return defaultValue;

            return array[index];
        }
    }
}
