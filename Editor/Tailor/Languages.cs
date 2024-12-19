using System.Collections.Generic;

namespace Code.Combat
{
    public class Languages
    {
        public static List<LanguageGroup> SupportedLanguages = new List<LanguageGroup>
        {
            new LanguageGroup
            {
                Region = "us",
                HumanName = "United States",
                Languages = new List<Language>
                {
                    new Language { HumanName = "English (US)", Code = "usen" },
                    new Language { HumanName = "Spanish (US)", Code = "uses" },
                    new Language { HumanName = "French (US)", Code = "usfr" }
                }
            },
            new LanguageGroup
            {
                Region = "eu",
                HumanName = "Europe",
                Languages = new List<Language>
                {
                    new Language { HumanName = "German (EU)", Code = "eude" },
                    new Language { HumanName = "English (EU)", Code = "euen" },
                    new Language { HumanName = "Spanish (EU)", Code = "eues" },
                    new Language { HumanName = "French (EU)", Code = "eufr" },
                    new Language { HumanName = "Italian (EU)", Code = "euit" }
                }
            },
            new LanguageGroup
            {
                Region = "jp",
                HumanName = "Japan",
                Languages = new List<Language>
                {
                    new Language { HumanName = "Japanese", Code = "jpja" }
                }
            },
            new LanguageGroup
            {
                Region = "cn",
                HumanName = "China",
                Languages = new List<Language>
                {
                    new Language { HumanName = "Chinese (Simplified)", Code = "cnch" }
                }
            },
            new LanguageGroup
            {
                Region = "tw",
                HumanName = "Taiwan",
                Languages = new List<Language>
                {
                    new Language { HumanName = "Chinese (Traditional)", Code = "twch" }
                }
            },
            new LanguageGroup
            {
                Region = "kr",
                HumanName = "Korea",
                Languages = new List<Language>
                {
                    new Language { HumanName = "Korean", Code = "krko" }
                }
            }
        };

        public class LanguageGroup
        {
            public string HumanName;
            public List<Language> Languages;
            public string Region;
        }

        public class Language
        {
            public string Code;
            public string HumanName;
        }
    }
}