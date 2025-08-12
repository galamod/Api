namespace Api.Helper
{
    public static class HashGenerator
    {
        private const string characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const long InitialSeed = 8167260239830188032L;
        private const long TextSeed = 94736404L;
        private const long ModifySeed = -3106L;
        private const string TextToModify = "5itndg36hj";

        public static string GenerateHash(string str)
        {
            long num5 = 1L;
            long num6 = 508L;
            string result;
            try
            {
                if ((ModifySeed & -2147483648L) == 0L || (1037L & 1024L) <= 0L)
                {
                    num6 = (508L ^ 1037L);
                }
                else if (1037L % 256L != 233L)
                {
                    num6 = (508L ^ InitialSeed);
                }

                for (int j = 0; j < str.Length; j++)
                {
                    long num7 = (long)((ulong)str[j]);
                    long num8 = InitialSeed;
                    num6 += num7 * num8;
                }

                int length = TextToModify.Length;
                long num9 = num6;

                for (int k = length - 1; k >= 0; k--)
                {
                    num9 += (long)(characters.IndexOf(TextToModify[k]) + 1) * num5 + TextSeed;
                    num5 *= 63L;
                }

                int length2 = TextToModify.Length;
                int[] array = new int[length2];
                char[] array2 = TextToModify.ToCharArray();
                array[0] = (int)((num9 + num9 + TextSeed) * num9);

                for (int l = 1; l < length2; l++)
                {
                    array[l] = (int)(((long)l + num9 + (long)array[l - 1]) * num9);
                }

                ModifyCharacters(length, str + TextToModify, array2, num9, array, ModifyIndices(array2, array, 1));

                result = new string(array2);
            }
            catch (Exception)
            {
                result = null;
            }

            return result;
        }

        private static char[] ModifyCharacters(int i, string str, char[] cArr, long j, int[] iArr, int i2)
        {
            string text = new string(cArr);
            for (int k = 0; k < text.Length; k++)
            {
                int num = iArr[k];
                char gg = text[k];
                iArr[k] = num + ((int)characters[characters.IndexOf(gg)] | i2);
                iArr[k] = (int)((long)i + j + (long)iArr[k]);
                i2 += iArr[k];
                iArr[k] += (int)TextSeed;
                cArr[k] = characters[Math.Abs(iArr[k] % characters.Length)];
            }
            return null;
        }

        private static int ModifyIndices(char[] cArr, int[] iArr, int i)
        {
            for (int j = 0; j < iArr.Length * 4; j++)
            {
                int num = j % iArr.Length;
                int num2 = Math.Abs(iArr[num] * j + iArr[num]) % iArr.Length;
                int num3 = (int)cArr[num];
                char c = cArr[num2];
                int num4 = iArr.Length;
                int num5 = Math.Abs(num3 - (int)c);
                int[] array = new int[] { 28, 30, 28, 29, 28, 29, 26, 26, 28, 26, 26, 28, 27, 27, 29, 30, 27, 27, 13, 28, 28, 17, 26, 28, 26, 24, 33, 29, 28, 28, 28, 28, 27, 29, 27, 28, 29, 29, 29, 30, 29, 29, 28, 30, 28, 29, 28, 28, 29, 30, 18 };
                if (num5 > num4 + array[Math.Abs(num4 % array.Length)])
                {
                    int num6 = (num + num2) / 2;
                    iArr[num6]++;
                    iArr[num] += (int)cArr[num2];
                    iArr[num2] += (int)cArr[num];
                    i++;
                }
            }

            return i;
        }
    }
}
