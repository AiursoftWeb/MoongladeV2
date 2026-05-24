using Aiursoft.MoongladeV2.Models;

namespace Aiursoft.MoongladeV2.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string Icp = "Icp";
    public const string CompanyAddress = "CompanyAddress";
    public const string CompanyPhone = "CompanyPhone";
    public const string CompanyEmail = "CompanyEmail";
    public const string CompanyPostcode = "CompanyPostcode";
    public const string ContractLogo = "ContractLogo";
    public const string ShowContractHeader = "ShowContractHeader";

    // ── AI: Chat / Translation (3 settings) ────────────────────────────────────
    public const string OpenAiChatEndpoint = "OpenAiChatEndpoint";
    public const string OpenAiModel = "OpenAiModel";
    public const string OpenAiApiToken = "OpenAiApiToken";

    // ── AI: Embedding / Vector Search (3 settings) ─────────────────────────────
    public const string EmbeddingEndpoint = "EmbeddingEndpoint";
    public const string EmbeddingModel = "EmbeddingModel";
    public const string EmbeddingApiToken = "EmbeddingApiToken";

    // ── AI: Feature switch (1 bool) ────────────────────────────────────────────
    public const string EnableEmbeddingBasedSearch = "EnableEmbeddingBasedSearch";

    // ── Localization ────────────────────────────────────────────────────────────
    public const string LocalizationLanguages = "LocalizationLanguages";
    public const string EmbeddingQueryCacheLimit = "EmbeddingQueryCacheLimit";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft MoongladeV2"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name of the company or project. E.g. Aiursoft."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer["The URL of the company or project. E.g. https://www.aiursoft.com"],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = CompanyAddress,
            Name = Localizer["Company Address"],
            Description = Localizer["The address of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "西京市中关村大街 999 号"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyPhone,
            Name = Localizer["Company Phone"],
            Description = Localizer["The phone number of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "010-12345678"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyEmail,
            Name = Localizer["Company Email"],
            Description = Localizer["The email address of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "anduin@aiursoft.com"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyPostcode,
            Name = Localizer["Company Postcode"],
            Description = Localizer["The postcode of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "100080"
        },
        new GlobalSettingDefinition
        {
            Key = ContractLogo,
            Name = Localizer["Contract Logo"],
            Description = Localizer["The logo of the contract displayed in the header. Support jpg, png, svg. Separate from system logo."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "contract-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = ShowContractHeader,
            Name = Localizer["Show Contract Header"],
            Description = Localizer["Whether to show the contract header (Logo, address, etc.) in the contract view."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },

        // ── AI: Chat / Translation ──────────────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = OpenAiChatEndpoint,
            Name = Localizer["OpenAI Chat Endpoint"],
            Description = Localizer["OpenAI-compatible chat completions endpoint used for post translation. E.g. https://ollama.example.com/v1/chat/completions. Leave empty to disable AI translation."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiModel,
            Name = Localizer["Localization Model"],
            Description = Localizer["LLM model name used for translating blog posts, e.g. qwen3:32b or gpt-4o. Must be available at the OpenAI Chat Endpoint above."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiApiToken,
            Name = Localizer["OpenAI API Token"],
            Description = Localizer["Bearer token for the OpenAI Chat Endpoint, e.g. sk-abc123. Leave empty if the endpoint does not require authentication."],
            Type = SettingType.Text,
            DefaultValue = ""
        },

        // ── AI: Embedding / Vector Search ───────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = EmbeddingEndpoint,
            Name = Localizer["Embedding Endpoint"],
            Description = Localizer["Ollama API base URL for generating document and query embeddings (vector search). Only the host is used — /api/embed is appended automatically. Falls back to OpenAI Chat Endpoint when empty. E.g. https://ollama.example.com"],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingModel,
            Name = Localizer["Embedding Model"],
            Description = Localizer["Embedding model name for vector search, e.g. bge-m3:latest. Must be available at the Embedding Endpoint."],
            Type = SettingType.Text,
            DefaultValue = "bge-m3:latest"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingApiToken,
            Name = Localizer["Embedding API Token"],
            Description = Localizer["Bearer token for the Embedding Endpoint. Falls back to OpenAI API Token when empty."],
            Type = SettingType.Text,
            DefaultValue = ""
        },

        // ── AI: Feature switch ──────────────────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = EnableEmbeddingBasedSearch,
            Name = Localizer["Enable Embedding-Based Search"],
            Description = Localizer["Master switch for semantic (vector-based) post search. When enabled and the embedding model is configured, search uses cosine similarity. Falls back silently to keyword search when disabled or not configured."],
            Type = SettingType.Bool,
            DefaultValue = "False"
        },

        // ── Localization ────────────────────────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = LocalizationLanguages,
            Name = Localizer["Localization Languages"],
            Description = Localizer["Comma-separated BCP-47 language codes to translate blog posts into, e.g. en-US,ja-JP,ko-KR,fr-FR. Leave empty to disable AI translation."],
            Type = SettingType.Text,
            DefaultValue = "en-US,zh-TW,ja-JP,ko-KR,de-DE,fr-FR,es-ES,ru-RU"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingQueryCacheLimit,
            Name = Localizer["Embedding Query Cache Limit"],
            Description = Localizer["Maximum number of cached search-query embeddings stored in the database (LRU). Default 2000."],
            Type = SettingType.Number,
            DefaultValue = "2000"
        }
    };
}
