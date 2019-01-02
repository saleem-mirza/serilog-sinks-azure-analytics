using System;
using System.Security.Cryptography;
using System.Text;

namespace Serilog.Sinks.AzureAnalytics
{
    internal class SignatureBuilder
    {
        private readonly string _workspaceId;
        private readonly string _primaryAuthKey;
        private readonly string _secondaryAuthKey;

        private bool _usePrimaryKey;

        public SignatureBuilder(string workspaceId, string primaryAuthKey, string secondaryAuthKey, bool usePrimaryKey = true)
        {
            _workspaceId = workspaceId;
            _primaryAuthKey = primaryAuthKey;
            _secondaryAuthKey = secondaryAuthKey;
            _usePrimaryKey = usePrimaryKey;
        }

        public void ToggleKeys()
        {
            _usePrimaryKey = !_usePrimaryKey;
        }

        public bool SupportsMultipleAuthKeys => !String.IsNullOrEmpty(_secondaryAuthKey);

        public string BuildSignature(int contentLength, string dateString)
        {
            var hashedKey = BuildHashedSignature(contentLength, dateString, GetKey());
            return $"SharedKey {_workspaceId}:{hashedKey}";
        }

        private string GetKey()
        {
            return _usePrimaryKey ? _primaryAuthKey : _secondaryAuthKey;
        }

        private string BuildHashedSignature(int contentLength, string dateString, string key)
        {
            var stringToHash =
                "POST\n" + contentLength + "\napplication/json\n" + "x-ms-date:" + dateString + "\n/api/logs";

            var encoding = new UTF8Encoding();
            var keyByte = Convert.FromBase64String(key);
            var messageBytes = encoding.GetBytes(stringToHash);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                return Convert.ToBase64String(hmacsha256.ComputeHash(messageBytes));
            }
        }
    }
}