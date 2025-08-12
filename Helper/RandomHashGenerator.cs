namespace Api.Helper
{
    internal class RandomHashGenerator
    {
        private static readonly char[] characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray();
        private readonly Random random = new Random();
        private char[] buffer;

        public RandomHashGenerator(int length)
        {
            if (length < 1)
                throw new ArgumentException("Length must be greater than zero.", nameof(length));

            buffer = new char[length];
        }

        public string GenerateRandomHash()
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = characters[random.Next(characters.Length)];
            }

            return new string(buffer);
        }
    }
}
