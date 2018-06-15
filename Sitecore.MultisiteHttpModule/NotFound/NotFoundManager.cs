﻿using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.MultisiteHttpModule.Configuration;
using Sitecore.Pipelines.HttpRequest;
using System;
using System.Configuration;
using System.Runtime.Remoting.Contexts;
using System.Web;
using Sitecore.SecurityModel;

namespace Sitecore.MultisiteHttpModule.NotFound
{
    public class NotFoundManager
    {
        private MultisiteHttpModuleSettings _settings;

        public void ProcessRequest(HttpRequestArgs args, HttpContext context)
        {
            EnsureSettingsArePopulated();
            if (RequestShouldBeProcessedAs404(args, context))
            {
                SwitchTo404Item();
            }          
        }

        private void EnsureSettingsArePopulated()
        {
            if (_settings == null)
            {
                _settings = ConfigurationManager.GetSection("multisiteHttpModule") as MultisiteHttpModuleSettings;
            }
        }

        private bool RequestShouldBeProcessedAs404(HttpRequestArgs args, HttpContext context)
        {
            return _settings.NotFoundEnabled
                   && IsSitecoreContextAvailable()
                   && !IsValidUrlRequest(context, args);
        }

        private void SwitchTo404Item()
        {
            var notFoundPageId = Context.Site.Properties[Settings.Constants.PropertyNames.NotFoundPageId];
            if (string.IsNullOrEmpty(notFoundPageId))
            {
                Log.Warn(String.Format("Sitecore.MultisiteHttpModule.NotFound: 404 page id not populated for site [{0}]", Context.Site.Name), this);
                return;
            }

            if (ID.IsID(notFoundPageId) || notFoundPageId.StartsWith(Settings.Constants.ContentPath))
            {
                Context.Item = Context.Site.Database.GetItem(notFoundPageId);
            }
        }

        private bool IsValidUrlRequest(HttpContext context, HttpRequestArgs args)
        {
            return IsContextItemPopulated()
                || ItemExistsButUserLacksPermissions(context, args)
                || IsSitecoreCmsClientRequest()
                || IsValidAlias(args)
                || ShouldUrlBeExcluded(context.Request.RawUrl)
                || IsRequestForPhysicalFile(context);
        }


        private static bool ItemExistsButUserLacksPermissions(HttpContext context, HttpRequestArgs args)
        {
            if (Context.Site == null)
                return false;
            if (args.PermissionDenied == true)
                return true;
            using (new SecurityDisabler())
            {
                var itemPath = Context.Site.ContentStartPath + Context.Site.StartItem + GetRawUrlWithoutQueryString(context);
                return Context.Database.GetItem(itemPath) != null;
            }
        }

        private static string GetRawUrlWithoutQueryString(HttpContext context)
        {
            var rawUrl = context.Request.RawUrl;
            if (rawUrl.Contains("?"))
            {
                rawUrl = rawUrl.Substring(0, rawUrl.IndexOf('?'));
            }
            return rawUrl;
        }

        private static bool IsSitecoreContextAvailable()
        {
            return Context.Site != null
                   && Context.Database != null;
        }

        private static bool IsContextItemPopulated()
        {
            return Context.Item != null;
        }

        private static bool IsSitecoreCmsClientRequest()
        {
            return Context.Site.Name == Settings.Constants.ShellSiteName;
        }

        private static bool IsRequestForPhysicalFile(HttpContext context)
        {
            return System.IO.File.Exists(context.Server.MapPath(CleanUrl(context.Request.RawUrl)));
        }

        private bool IsValidAlias(HttpRequestArgs args)
        {
            var aliasExist = Context.Database.Aliases.Exists(args.LocalPath);
            return aliasExist && HasValidTargetItem(args);
        }

        private bool HasValidTargetItem(HttpRequestArgs args)
        {
            var isValid = false;
            var targetId = Context.Database.Aliases.GetTargetID(args.LocalPath);
            var targetUrl = Context.Database.Aliases.GetTargetUrl(args.LocalPath);

            if (!targetId.IsNull)
            {
                isValid = TryGetTargetItem(args, targetId);
            }
            else if (targetUrl.Length > 0)
            {
                isValid = true;
            }

            return isValid;
        }

        private bool TryGetTargetItem(HttpRequestArgs args, ID targetId)
        {
            var item = args.GetItem(targetId);
            if (item != null)
            {
                return true;
            }

            Log.Warn(String.Format("Sitecore.MultisiteHttpModule.NotFound: Found alias [{0}] but the target item does not exist", args.LocalPath), this);
            return false;
        }

        private bool ShouldUrlBeExcluded(string url)
        {
            var shouldBeExcluded = false;
            foreach (Exclude404Rule rule in _settings.Exclude404Rules)
            {
                switch (rule.Type)
                {
                    case Settings.ExcludeRuleType.Contains:
                        shouldBeExcluded = url.Contains(rule.Match);
                        break;
                    case Settings.ExcludeRuleType.StartsWith:
                        shouldBeExcluded = url.StartsWith(rule.Match);
                        break;
                    case Settings.ExcludeRuleType.EndsWith:
                        shouldBeExcluded = url.EndsWith(rule.Match);
                        break;
                }

                if (shouldBeExcluded)
                    break;
            }

            return shouldBeExcluded;
        }

        private static string CleanUrl(string rawval)
        {
            return rawval.Replace("*", String.Empty)
                .Replace("?", String.Empty)
                .Replace("<", String.Empty)
                .Replace(">", String.Empty)
                .Replace(",", String.Empty)
                .Replace(":", String.Empty)
                .Replace(";", String.Empty)
                .Replace("'", String.Empty)
                .Replace("\"", String.Empty)
                .Replace("[", String.Empty)
                .Replace("]", String.Empty)
                .Replace("//", "/")
                .Replace("\\\\", "\\");
        }
    }
}
