namespace Kariyer.Mail.Api.Common.Configuration;

public static class EmailSettingsExtensions{
    extension (EmailSettings target){
        public string FormattedFromAddress =>
            string.IsNullOrWhiteSpace(target.FromName)
                ? target.FromAddress
                : $"\"{target.FromName}\" <{target.FromAddress}>";
    }
}