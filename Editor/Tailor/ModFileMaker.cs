using System.Collections.Generic;
using System.IO;
using Scriban;
using UnityEngine;

namespace Code.Combat
{
    public class ModFileMaker
    {
        private static readonly string xml_path = "Assets/Outputs/Mod/patches/xml/";

        private static Template LoadTemplate(string templateName)
        {
            var rawFile = Resources.Load("Templates/" + templateName) as TextAsset;
            if (rawFile == null)
            {
                Debug.LogError(templateName + " not found");
                return null;
            }

            return Template.Parse(rawFile.text);
        }

        public static void MakeShopFile(List<CostumeModArgs> args)
        {
            var shopTemplate = LoadTemplate("Shop.template");
            var shopLineTemplate = LoadTemplate("ShopLine.template");

            var itemLines = new List<string>();
            foreach (var costumeModArgs in args)
                itemLines.Add(shopLineTemplate.Render(new
                {
                    costumeModArgs.Aid
                }));

            var result = shopTemplate.Render(new
            {
                lines = itemLines
            });

            // Ensure the Assets/Outputs folder exists
            Directory.CreateDirectory(xml_path);
            // Write file to Assets/Outputs folder
            File.WriteAllText(xml_path + "/Shop.xml", result);
        }

        public static void MakeAssetTableFile(List<CostumeModArgs> args)
        {
            var assetTableTemplate = LoadTemplate("AssetTable.template");
            var assetTableLineTemplate = LoadTemplate("AssetTableLine.template");
            var itemLines = new List<string>();
            foreach (var costumeModArgs in args)
            {
                var genderString = costumeModArgs.Gender == Gender.Female ? "女装" : "男装";
                itemLines.Add(assetTableLineTemplate.Render(new
                {
                    costumeModArgs.Aid,
                    costumeModArgs.DressModel,
                    genderString,
                    comment = $"{costumeModArgs.Name}, made with Divine Dragon Builder!"
                }));
            }

            var result = assetTableTemplate.Render(new
            {
                lines = itemLines
            });
            // Ensure the Assets/Outputs folder exists
            Directory.CreateDirectory(xml_path);
            // Write file to Assets/Outputs folder
            File.WriteAllText(xml_path + "/AssetTable.xml", result);
        }

        public static void MakeItemFile(List<CostumeModArgs> args)
        {
            var itemTemplate = LoadTemplate("Item.template");
            var itemLineTemplate = LoadTemplate("ItemLine.template");
            var itemLines = new List<string>();
            foreach (var costumeModArgs in args)
            {
                string genderString;
                if (costumeModArgs.Gender == Gender.Female)
                    genderString = "2";
                else
                    genderString = "1";
                itemLines.Add(itemLineTemplate.Render(new
                {
                    costumeModArgs.Aid,
                    costumeModArgs.Maid,
                    costumeModArgs.MaidH,
                    Gender = genderString
                }));
            }

            var result = itemTemplate.Render(new
            {
                lines = itemLines
            });

            // Ensure the Assets/Outputs folder exists
            Directory.CreateDirectory(xml_path);
            // Write file to Assets/Outputs folder
            File.WriteAllText(xml_path + "/Item.xml", result);
        }

        public static void MakeAccessoriesFile(List<CostumeModArgs> args)
        {
            var accessoryTemplate = LoadTemplate("Accessories.template");

            foreach (var languageGroup in Languages.SupportedLanguages)
            foreach (var language in languageGroup.Languages)
            {
                var accesoryFile = "";
                foreach (var costumeModsArgs in args)
                {
                    var costume = costumeModsArgs.Costume;
                    string title = null;
                    if (costume.LocalizedStrings.ContainsKey(language.Code))
                        title = costume.LocalizedStrings[language.Code]?.Title;
                    string description = null;
                    if (costume.LocalizedStrings.ContainsKey(language.Code))
                        description = costume.LocalizedStrings[language.Code]?.Description;

                    if (title == null && description == null)
                        continue;

                    accesoryFile += accessoryTemplate.Render(new
                    {
                        costumeModsArgs.Maid,
                        costumeModsArgs.MaidH,
                        Title = title,
                        Description = description
                    });
                    accesoryFile += "\n";
                }

                var path = $"Assets/Outputs/Mod/patches/msbt/message/{languageGroup.Region}/{language.Code}";
                Directory.CreateDirectory(path);
                File.WriteAllText(path + "/accessories.txt", accesoryFile);
            }
        }

        public class CostumeModArgs
        {
            public readonly string Aid;
            public readonly Costume Costume;
            public readonly string DressModel;
            public readonly Gender Gender;
            public readonly string Maid;
            public readonly string MaidH;
            public readonly string Name;

            public CostumeModArgs(string dressModel, Costume costume)
            {
                Name = costume.Name;
                Aid = $"AID_{costume.Name}";
                Maid = $"MAID_{costume.Name}";
                MaidH = $"MAID_H_{costume.Name}";
                DressModel = dressModel;
                Gender = costume.gender;
                Costume = costume;
            }
        }
    }
}