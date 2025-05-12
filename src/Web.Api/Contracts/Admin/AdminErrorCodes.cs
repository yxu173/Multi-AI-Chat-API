namespace Web.Api.Contracts.Admin;

public static class AdminErrorCodes
{
    // Provider API Key errors
    public const string ProviderApiKeyNotFound = "PROVIDER_API_KEY_NOT_FOUND";
    public const string ProviderNotFound = "PROVIDER_NOT_FOUND";
    public const string InvalidApiKey = "INVALID_API_KEY";
    
    // Subscription Plan errors
    public const string SubscriptionPlanNotFound = "SUBSCRIPTION_PLAN_NOT_FOUND";
    public const string InvalidSubscriptionPlan = "INVALID_SUBSCRIPTION_PLAN";
    
    // User Subscription errors
    public const string UserSubscriptionNotFound = "USER_SUBSCRIPTION_NOT_FOUND";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string SubscriptionExpired = "SUBSCRIPTION_EXPIRED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    
    // Permission errors
    public const string AdminRoleRequired = "ADMIN_ROLE_REQUIRED";
    public const string Unauthorized = "UNAUTHORIZED";
}
