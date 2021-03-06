﻿using NSoup;
using NSoup.Nodes;
using Sdl.Web.Common;
using Sdl.Web.Common.Models;
using Sdl.Web.Mvc.Configuration;
using Sdl.Web.Mvc.Controllers;
using SDL.DXA.Modules.CampaignContent.Models;
using SDL.DXA.Modules.CampaignContent.Provider;
using System;
using System.Web.Configuration;
using System.Web.Mvc;

namespace SDL.DXA.Modules.CampaignContent.Controllers
{
    /// <summary>
    /// Campaign Content Controller
    /// </summary>
    public class CampaignContentController : BaseController
    {
        private readonly SiteBaseUrlProvider _siteBaseUrlProvider;
        private readonly bool _useUrlParametersForLocalImages = true;

        public CampaignContentController(SiteBaseUrlProvider siteBaseUrlProvider = null)
        {
            _siteBaseUrlProvider = siteBaseUrlProvider ?? new SiteBaseUrlProvider();
            var useUrlParametersConfig = WebConfigurationManager.AppSettings["instant-campaign-use-parameters-for-local-images"];
            if (useUrlParametersConfig != null)
            {
                _useUrlParametersForLocalImages = bool.Parse(useUrlParametersConfig);
            }       
        }
	
        /// <summary>
        /// Assembly Content
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="containerSize"></param>
        /// <returns></returns>
        public ActionResult AssemblyContent(EntityModel entity, int containerSize = 0)
        {
            SetupViewData(entity, containerSize);

            CampaignContentZIP contentZip = (CampaignContentZIP) entity;

            // Process markup
            //
            ProcessMarkup(contentZip);

            return View(entity.MvcData.ViewName, entity);
        }

        /// <summary>
        /// Process markup. Blend the markup in the ZIP file and CMS content.
        /// </summary>
        /// <param name="campaignContentZip"></param>
        protected void ProcessMarkup(CampaignContentZIP campaignContentZip)
        {
            CampaignContentMarkup campaignContentMarkup = CampaignAssetProvider.Instance.GetCampaignContentMarkup(campaignContentZip, WebRequestContext.Localization);

            // Throw exception if main HTML is not found
            //
            if ( campaignContentMarkup.MainHtml == null )
            {
                throw new DxaException("No markup defined for campaign with ID: " + campaignContentZip.Id);
            }

            var htmlDoc = NSoupClient.Parse("<body>" + campaignContentMarkup.MainHtml + "</body>");

            // Remove all nodes that are marked as Preview Only
            //
            if (!WebRequestContext.IsPreview)
            {
                foreach (var element in htmlDoc.Body.Select("[data-preview-only=true]"))
                {
                    element.Remove();
                }
            }

            // Inject content into placeholders in the markup
            //
            if (campaignContentZip.TaggedContent != null)
            {
                int index = 1;
                foreach (var taggedContent in campaignContentZip.TaggedContent)
                {
                    foreach (var element in htmlDoc.Body.Select("[data-content-name=" + taggedContent.Name + "]"))
                    {
                        String contentMarkup = taggedContent.Content?.ToString() ?? string.Empty;

                        if (WebRequestContext.IsPreview)
                        {
                            contentMarkup = "<!-- Start Component Field: {\"XPath\":\"tcm:Metadata/custom:Metadata/custom:taggedContent[" +
                                index +
                                "]/custom:content[1]\"} -->" + contentMarkup;
                        }

                        element.Html(contentMarkup);
                    }
                    index++;
                }
            }

            // Inject tagged properties
            //
            if ( campaignContentZip.TaggedProperties != null)
            {
                int index = 1;
                foreach (var taggedProperty in campaignContentZip.TaggedProperties)
                {
                    var indexSuffix = taggedProperty.Index != null && taggedProperty.Index > 1 ? "-" + taggedProperty.Index : "";
                    foreach (var element in htmlDoc.Body.Select("[data-property-name" + indexSuffix + "=" + taggedProperty.Name + "]"))
                    {
                        var propertyValue = taggedProperty.Value;
                        var containsUrlPlaceholder = propertyValue.Contains("%URL%");
                        if ( taggedProperty.Image != null )
                        {
                            propertyValue = propertyValue.Replace("%URL%", taggedProperty.Image.Url);
                            if (element.TagName().Equals("img", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(taggedProperty.ImageAltText))
                            {
                                element.Attr("alt", taggedProperty.ImageAltText);
                            }

                            if (element.Attr("data-property-sibling-replace") == "true")
                            {
                                foreach (var sibling in element.SiblingElements)
                                {
                                    var siblingPropertyValue = sibling.Attr(taggedProperty.Target) ?? string.Empty;
                                    siblingPropertyValue = siblingPropertyValue.Replace("%URL%", taggedProperty.Image.Url);
                                    sibling.Attr(taggedProperty.Target, siblingPropertyValue);
                                    if (!string.IsNullOrEmpty(taggedProperty.ImageAltText) && sibling.TagName().Equals("img", StringComparison.OrdinalIgnoreCase))
                                    {
                                        sibling.Attr("alt", taggedProperty.ImageAltText);
                                    }
                                }
                            }
                        }
                        element.Attr(taggedProperty.Target, propertyValue ?? string.Empty);

                        // Generate image XPM markup if tagged property can contain/contains an image URL
                        //
                        if (WebRequestContext.IsPreview && containsUrlPlaceholder)
                        {
                            string xpmMarkup =
                                 "<!-- Start Component Field: {\"XPath\":\"tcm:Metadata/custom:Metadata/custom:taggedProperties[" +
                                index +
                                "]/custom:image[1]\"} -->";
                            element.Prepend(xpmMarkup);
                        }
                    }
                    index++;
                }
            }

            // Inject tagged links
            //
            if ( campaignContentZip.TaggedLinks != null )
            {
                int index = 1;
                foreach (var taggedLink in campaignContentZip.TaggedLinks)
                {
                    foreach (var element in htmlDoc.Body.Select("[data-link-name=" + taggedLink.Name + "]"))
                    {
                        var link = taggedLink.ComponentLink ?? taggedLink.Url;
                        element.Attr("href", link ?? "#");
                        if (!string.IsNullOrEmpty(taggedLink.Target))
                        {
                            element.Attr("target", taggedLink.Target);
                        }
                        if (WebRequestContext.IsPreview)
                        {
                            var fieldName = taggedLink.ComponentLink != null || string.IsNullOrWhiteSpace(taggedLink.Url) ? "componentLink" : "url";
                            string xpmMarkup =
                                 "<!-- Start Component Field: {\"XPath\":\"tcm:Metadata/custom:Metadata/custom:taggedLinks[" +
                                index +
                                "]/custom:" + fieldName + "[1]\"} -->";
                            element.Prepend(xpmMarkup);
                        }
                    }
                    index++;
                }
            }
            string assetBaseDir = this.GetAssetBaseDir(campaignContentZip);

            // Process assets
            //
            this.ProcessAssetLinks(htmlDoc, assetBaseDir, "href");
            this.ProcessAssetLinks(htmlDoc, assetBaseDir, "src");

            // Inject tagged images
            //
            if (campaignContentZip.TaggedImages != null)
            {
                int index = 1;
                foreach (var taggedImage in campaignContentZip.TaggedImages)
                {
                    foreach (var element in htmlDoc.Body.Select("[data-image-name=" + taggedImage.Name + "]"))
                    {
                        var imageUrl = string.Empty;
                        if (taggedImage.Image != null && taggedImage.Image.Url != null)
                        {
                            imageUrl = taggedImage.Image.Url;
                        } 
                        else if (taggedImage.ImageUrl != null)
                        {
                            imageUrl = taggedImage.ImageUrl;
                        }
                        if (taggedImage.Parameters != null &&
                            (taggedImage.Image == null || 
                             (taggedImage.Image != null && _useUrlParametersForLocalImages)))
                        {
                            imageUrl += "?" + taggedImage.Parameters;
                        }
                        element.Attr("src", imageUrl);

                        if (!string.IsNullOrEmpty(taggedImage.AltText))
                        {
                            element.Attr("alt", taggedImage.AltText);
                        }

                        if (WebRequestContext.IsPreview)
                        {
                            string xpmMarkup =
                                 "<!-- Start Component Field: {\"XPath\":\"tcm:Metadata/custom:Metadata/custom:taggedImages[" +
                                index +
                                "]/custom:image[1]\"} -->";
                            
                            if (taggedImage.Image != null)
                            {
                                element.Before(xpmMarkup);
                            }
                            else
                            {
                                // Surround the XPM markup in an additional span. This to avoid that the
                                // image with absolute URL will not disappear as soon you click on it.
                                //
                                element.Before("<span>" + xpmMarkup + "</span>");
                            }

                        }
                    }
                    index++;
                }
            }

            // Insert header markup (JS, CSS etc)
            //
            if ( campaignContentMarkup.HeaderHtml != null )
            {
                var headerDoc = NSoupClient.Parse("<body>" + campaignContentMarkup.HeaderHtml + "</body>");
                this.ProcessAssetLinks(headerDoc, assetBaseDir, "src");
                this.ProcessAssetLinks(headerDoc, assetBaseDir, "href");
                PluggableMarkup.RegisterMarkup("css", headerDoc.Body.Html());
                // TODO: Should it be called top-js?? Or have a top-css injection point as well
            }

            // Insert footer markup (JS etc)
            //
            if ( campaignContentMarkup.FooterHtml != null )
            {
                var footerDoc = NSoupClient.Parse("<body>" + campaignContentMarkup.FooterHtml + "</body>");
                this.ProcessAssetLinks(footerDoc, assetBaseDir, "src");
                PluggableMarkup.RegisterMarkup("bottom-js", footerDoc.Body.Html());
            }

            campaignContentZip.ProcessedContent = new RichTextFragment(htmlDoc.Body.Html()).ToHtml();

        }

        /// <summary>
        /// Process asset links so they refer to correct exposed campaign path
        /// </summary>
        /// <param name="htmlDoc"></param>
        /// <param name="assetBaseDir"></param>
        /// <param name="attributeName"></param>
        protected void ProcessAssetLinks(Document htmlDoc, String assetBaseDir, String attributeName)
        {
            foreach (var element in htmlDoc.Body.Select("[" + attributeName + "]"))
            {
                String assetUrl = element.Attr(attributeName);
                if (!assetUrl.StartsWith("/") && !assetUrl.StartsWith("http") && !assetUrl.StartsWith("#"))
                {
                    assetUrl = assetBaseDir + "/" + assetUrl;
                    element.Attr(attributeName, assetUrl);
                }
            }
        }

        /// <summary>
        /// Get asset base dir (which are exposed on the web page) for the specified campaign ZIP
        /// </summary>
        /// <param name="campaignContentZip"></param>
        /// <returns></returns>
        protected String GetAssetBaseDir(CampaignContentZIP campaignContentZip)
        {
            return _siteBaseUrlProvider.GetSiteBaseUrl() + "/assets/campaign/" + campaignContentZip.Id;
        }

    }

}
