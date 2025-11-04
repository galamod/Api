using System.Web;

namespace Api.FreeKassa
{
    internal class CustomParameters
    {
        private const string CustomFieldPrefix = "us_";
        private IList<KeyValuePair<string, string>> _values = new List<KeyValuePair<string, string>>();
        public IList<KeyValuePair<string, string>> GetParameters => _values.OrderBy(x => x.Key).ToList();

        public void Add(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));

            if (key.StartsWith(CustomFieldPrefix, StringComparison.InvariantCultureIgnoreCase))
                key = key.Remove(0, CustomFieldPrefix.Length);

            _values.Add(new KeyValuePair<string, string>(CustomFieldPrefix + key, value));
        }

        public override string ToString()
        {
            return string.Join(":", GetParameters.Select(x => $"{x.Key}={HttpUtility.UrlEncode(x.Value)}"));
        }
    }
}
