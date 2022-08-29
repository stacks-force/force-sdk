﻿using StacksForce.Stacks.WebApi;
using StacksForce.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace StacksForce.Stacks.Metadata
{
    public class FungibleTokenMetaData
    {
        private static readonly JsonSerializerOptions SERIALIZER_OPTIONS = new JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

        private static readonly FungibleTokenMetaData Empty = new FungibleTokenMetaData();

        public string? Currency { get; private set; }        
        public string? Name { get; private set; }
        public string? Description { get; private set; }
        public string? Image { get; private set; }

        public uint Decimals { get; private set; }

        static public FungibleTokenMetaData FromJson(string? json)
        {
            try
            {
                var data = JsonSerializer.Deserialize<JsonFormat>(json, SERIALIZER_OPTIONS)!;
                return new FungibleTokenMetaData { Name = data.name, Description = data.properties?.description, Image = data.image };

            }
            catch (Exception e)
            {
                Log.Debug("FungibleTokenMetaData.FromJson failed: " + e);
                return Empty;
            }
        }

        static public async Task<FungibleTokenMetaData> ForTokenContract(Blockchain chain, string tokenContractId)
        {
            var addressAndContract = tokenContractId.Split(".");
            if (addressAndContract.Length != 2)
                throw new ArgumentException("Incorrect token contract id");

            var result = await chain.GetFungibleTokenMetadata(tokenContractId).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                var d = result.Data;
                var imageUrl = String.IsNullOrEmpty(d.image_uri) ? d.image_canonical_uri : d.image_uri;
                var description = d.description;
                if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(d.token_uri))
                {
                    var tokenDataFromFile = await HttpHelper.SendRequest(HttpHelper.GetHttpUrlFrom(d.token_uri)).ConfigureAwait(false);
                    if (tokenDataFromFile.IsSuccess)
                    {
                        try
                        {
                            var file = JsonSerializer.Deserialize<MetaDataFile>(tokenDataFromFile.Data, SERIALIZER_OPTIONS);
                            if (file != null)
                            {
                                description = file.description;
                                imageUrl = file.image;

                                if (file.properties != null) {
                                    if (string.IsNullOrEmpty(imageUrl) && file.properties.TryGetValue("image", out var imageObj))
                                        imageUrl = imageObj.description;
                                    if (string.IsNullOrEmpty(description) && file.properties.TryGetValue("description", out var descrObj))
                                        description = descrObj.description;
                                }
                            }
                        } catch (Exception e)
                        {
                            Log.Debug("FungibleTokenMetaData.ForTokenContract: " + e);
                        }
                    }
                }
                return new FungibleTokenMetaData { Currency = d.symbol, Name = d.name, Description = description, Image = imageUrl, Decimals = d.decimals };
            } else
            {
                var symbolResult = await chain.ReadonlyGetString(addressAndContract[0], addressAndContract[1], "get-symbol").ConfigureAwait(false);
                if (symbolResult.IsSuccess && !string.IsNullOrEmpty(symbolResult.Data))
                {
                    var nameResult = await chain.ReadonlyGetString(addressAndContract[0], addressAndContract[1], "get-name").ConfigureAwait(false);
                    var decimalsResult = await chain.ReadonlyGetUlong(addressAndContract[0], addressAndContract[1], "get-decimals").ConfigureAwait(false);
                    return new FungibleTokenMetaData { Currency = symbolResult.Data, Name = nameResult.Data, Decimals = decimalsResult.IsSuccess ? (uint) decimalsResult.Data : 0 };
                }
            }
            return Empty;
        }

        static public async Task<FungibleTokenMetaData> FromUrl(string url)
        {
            string? data = null;
            url = HttpHelper.GetHttpUrlFrom(url);
            var r = await HttpHelper.SendRequest(url).ConfigureAwait(false);
            if (r.IsSuccess)
                data = r.Data;
            return FromJson(data);
        }

        public override string ToString()
        {
            return $"Fungible token: {Name}({Description}) {Image}";
        }

        private class JsonFormat
        {
            public string name;
            public string image;
            public object[] attributes;
            public Properties properties;
            public class Properties
            {
                public string description;
            }
        }

        private class MetaDataFile
        {
            public Dictionary<string, PropObj> properties;

            public string name;
            public string description;
            public string image;

            public class PropObj
            {
                public string type;
                public string description;
            }
        }
    }
}
