﻿using Sdl.Web.Common.Models;

namespace SDL.DXA.Modules.CampaignContent.Models
{
    [SemanticEntity(EntityName = "TaggedProperty", Vocab = CoreVocabulary, Prefix = "e")]
    public class TaggedProperty : EntityModel
    {
        [SemanticProperty("e:name")]
        public string Name { get; set; }

        [SemanticProperty("e:value")]
        public string Value { get; set; }

        [SemanticProperty("e:image")]
        public MediaItem Image { get; set; }

        [SemanticProperty("e:imageAltText")]
        public string ImageAltText { get; set; }

        [SemanticProperty("e:target")]
        public string Target { get; set; }

        [SemanticProperty("e:index")]
        public int? Index { get; set; }

    }
}