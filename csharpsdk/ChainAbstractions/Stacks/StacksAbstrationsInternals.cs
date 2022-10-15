﻿using ChainAbstractions.Stacks.ContractWrappers;
using StacksForce.Stacks.WebApi;
using StacksForce.Stacks;
using StacksForce.Stacks.ChainTransactions;
using StacksForce.Stacks.Metadata;
using StacksForce.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChainAbstractions.Stacks
{
    static public partial class StacksAbstractions
    {

        private class FungibleTokenData : IFungibleTokenData
        {
            public string Code { get; }
            public string Description { get; }
            public string ImageUrl { get; }
            public uint Decimals { get; }


            public string Address { get; }
            public string Contract { get; }
            public string Id { get; }

            public FungibleTokenData(string fullId, FungibleTokenMetaData metaData)
            {
                var tokenIdData = StacksForce.Stacks.Address.ParseFromFullTokenId(fullId);
                Address = tokenIdData.address;
                Contract = tokenIdData.contract;
                Id = tokenIdData.token;

                Code = metaData.Currency;
                if (string.IsNullOrEmpty(Code))
                    Code = tokenIdData.token;

                Description = metaData.Description;
                if (string.IsNullOrEmpty(Description))
                    Description = metaData.Name;

                ImageUrl = metaData.Image;
                Decimals = metaData.Decimals;
            }

            public FungibleTokenData(string code, string description, string imageUrl, uint decimals)
            {
                Code = code;
                Description = description;
                ImageUrl = imageUrl;
                Decimals = decimals;
            }

            public string FormatCount(ulong count) => FormatBalance(count, Decimals, Code);
        }

        private class StxTokenData : FungibleTokenData
        {
            public const string STX_CODE = "STX";
            public const uint DECIMALS = 6;
            public const string DESCRIPTION = "Stacks blockchain token";
            public const string IMG_URL = "https://assets-global.website-files.com/618b0aafa4afde65f2fe38fe/618b0aafa4afde785dfe397d_icon-stacks-gradient.svg";

            public StxTokenData() : base(STX_CODE, DESCRIPTION, IMG_URL, DECIMALS) { }
        }

        private class FungibleToken : IFungibleToken
        {
            public ulong Balance { get; }
            public IFungibleTokenData Data => _data;

            private readonly FungibleTokenData _data;

            public FungibleToken(ulong balance, FungibleTokenData data)
            {
                Balance = balance;
                _data = data;
            }

            public string FormatCount(ulong count) => FormatBalance(count, _data.Decimals, _data.Code);
        }

        private class BlockchainImplBasic : IBlockchain
        {
            internal readonly Blockchain _blockchain;

            public FTMetaDataCache FTCache { get; private set; }

            public BlockchainImplBasic(Blockchain blockchain)
            {
                _blockchain = blockchain;
                FTCache = new FTMetaDataCache(blockchain);
            }

            public IBasicWallet CreateNewWallet()
            {
                return BasicWalletImpl.CreateNew(_blockchain);
            }

            public IBasicWallet? GetWalletForMnemonic(string mnemonic)
            {
                return BasicWalletImpl.FromMnemonic(_blockchain, mnemonic);
            }

            public IWalletInfo? GetWalletInfoForAddress(string address)
            {
                if (string.IsNullOrEmpty(address))
                    return null;

                if (address.Contains("."))
                {
                    var addressAndContract = address.Split('.');
                    if (addressAndContract.Length != 2)
                        return null;
                }
                else
                {
                    var a = Address.FromC32(address);
                    if (a == null)
                        return null;
                }

                return new BasicWalletInfo(_blockchain, address);
            }
        }

        private class TestNetImpl : BlockchainImplBasic
        {
            public TestNetImpl() : base(Blockchains.Testnet) { }
        }

        private class MainNetImpl : BlockchainImplBasic
        {
            public MainNetImpl() : base(Blockchains.Mainnet) { }
        }

        private class BasicWalletImpl : BasicWalletInfo, IBasicWallet
        {
            internal readonly StacksWallet _wallet;
            internal readonly TransactionsManager _manager;

            static public BasicWalletImpl? FromMnemonic(Blockchain chain, string mnemonic)
            {
                if (string.IsNullOrEmpty(mnemonic))
                    return null;
                try
                {
                    var wallet = new StacksWallet(mnemonic);
                    return new BasicWalletImpl(chain, wallet);
                } catch (Exception e)
                {
                    return null;
                }                
            }

            static public IBasicWallet CreateNew(Blockchain chain)
            {
                return FromMnemonic(chain, StacksWallet.GenerateMnemonicPhrase())!;
            }

            public BasicWalletImpl(Blockchain chain, StacksWallet wallet) : base(chain, wallet.GetAccount(0).GetAddress(chain.GetAddressVersion()))
            {
                _wallet = wallet;
                _manager = new TransactionsManager(chain, wallet.GetAccount(0));
            }

            public string GetMnemonic() => _wallet.Mnemonic;

            public async Task<ITransaction> GetTransferTransaction(IFungibleToken token, string recepient, string? memo = null)
            {
                if (Address.FromC32(recepient) == null)
                {
                    return new TransactionWrapper(null, new Error("Incorrect recepient"));
                }
                if (token.Data.Code == Stx.Code)
                {
                    var result = await _manager.GetStxTransfer(recepient, token.Balance, memo).ConfigureAwait();
                    return new TransactionWrapper(_manager, result);
                }
                else
                {
                    var ftData = token.Data as FungibleTokenData;
                    return await SIP10.Transfer(ftData.Address, ftData.Contract, ftData.Id, this, token.Balance, GetAddress(), recepient, memo).ConfigureAwait();
                }
            }
        }

        public sealed class TransactionWrapper : ITransaction
        {
            public TransactionState State { get { UpdateState(); return _state; } }

            public Error? Error { get { UpdateState(); return _error; } }

            public IFungibleToken? Cost => _transaction != null && _transaction.Fee > 0 ? new FungibleToken(_transaction.Fee, Stx as FungibleTokenData) : null;

            private TransactionState _state = TransactionState.Unknown;
            private Error? _error;
            internal TransactionInfo? _info;
            internal Transaction? _transaction;
            private TransactionsManager? _manager;

            public TransactionWrapper(TransactionInfo? info, Error? error)
            {
                _info = info;
                _error = error;
            }

            public TransactionWrapper(TransactionsManager manager, AsyncCallResult<Transaction> transactionResult)
            {
                _manager = manager;
                _transaction = transactionResult.Data;
                _error = transactionResult.Error;
            }

            private void UpdateState()
            {
                if (_error != null)
                {
                    _state = TransactionState.Failed;
                    return;
                }

                if (_transaction != null && _info == null)
                {
                    _state = TransactionState.Unknown;
                    return;
                }

                if (_info == null)
                {
                    _state = TransactionState.Failed;
                    return;
                }

                if (_info.Status == TransactionStatus.Pending)
                    _state = TransactionState.Pending;
                else if (_info.Status == TransactionStatus.Success)
                    _state = _info.IsAnchored ? TransactionState.Approved : TransactionState.PreApproved;
                else
                {
                    _state = TransactionState.Failed;
                    _error = new Error(_info.Status.ToString());
                }
            }

            public override string ToString()
            {
                return Error != null ? $"Error: {Error}" : $"State = {State}";
            }

            public async Task<Error> Send(IFungibleToken? newCost)
            {
                if (State == TransactionState.PreApproved || State == TransactionState.Approved)
                {
                    return new Error("Incorrect transaction state");
                }

                if (_transaction == null)
                {
                    return new Error("Not found");
                }

                if (newCost != null)
                    _transaction.UpdateFeeAndNonce(newCost.Balance, _transaction.Nonce);

                var result = await _manager.Run(_transaction).ConfigureAwait();

                _info = result.Data;
                _error = result.Error;

                return _error;
            }
        }

        private class BasicWalletInfo : IWalletInfo
        {
            private readonly Blockchain _chain;
            private readonly string _address;

            public event Action OnBalanceChanged;

            public BasicWalletInfo(Blockchain chain, string address)
            {
                _chain = chain;
                _address = address;
            }

            public async Task<IFungibleToken?> GetToken(string tokenId)
            {
                if (string.IsNullOrEmpty(tokenId))
                    tokenId = Stx.Code;

                var result = await _chain.GetBalances(_address).ConfigureAwait();
                if (result.IsSuccess)
                {
                    if (tokenId == Stx.Code)
                        return new FungibleToken(result.Data.stx.balance, (FungibleTokenData)Stx);

                    if (result.Data.fungible_tokens.TryGetValue(tokenId, out var info))
                    {
                        var tokenData = await _chain.FTCache().Get(tokenId).ConfigureAwait();
                        return new FungibleToken(info.balance, tokenData);
                    }
                }

                return null;
            }

            public IDataStream<INFT> GetNFTs(string? nftType = null, bool readMetaData = true)
            {
                return new NFTStream(_chain, _address, nftType, readMetaData);
            }

            public async Task<List<IFungibleToken>> GetAllTokens()
            {
                var fts = new List<IFungibleToken>();
                var result = await _chain.GetBalances(_address).ConfigureAwait();
                if (result.IsSuccess)
                {
                    fts.Add(new FungibleToken(result.Data.stx.balance, (StxTokenData)Stx));
                    foreach (var ft in result.Data.fungible_tokens)
                    {
                        if (ft.Value.balance > 0)
                        {
                            var tokenData = await _chain.FTCache().Get(ft.Key).ConfigureAwait();
                            fts.Add(new FungibleToken(ft.Value.balance, tokenData));
                        }
                    }
                }
                return fts;
            }

            public string GetAddress() => _address;
        }

        private class NFTStream : BasicDataStream<INFT>
        {
            private Blockchain _chain;
            private string _address;
            private string? _nftType;
            private bool _readMetaData;

            public NFTStream(Blockchain chain, string address, string? nftType, bool readMetaData)
            {
                _chain = chain;
                _address = address;
                _nftType = nftType;
                _readMetaData = readMetaData;
            }

            protected override async Task<List<INFT>?> GetRange(long index, long count)
            {
                var nfts = new List<INFT>();
                var result = await _chain.GetNFTHoldings(_address, _nftType != null ? new string[] { _nftType } : null, false, (ulong)count, (ulong)index).ConfigureAwait();
                if (result.IsSuccess)
                {
                    foreach (var t in result.Data.results)
                    {
                        var data = t.Extract();

                        if (_readMetaData)
                            nfts.Add(await NFTUtils.GetFrom(data.address, data.contract, data.nft, data.id).ConfigureAwait());
                        else
                            nfts.Add(new NFTUtils.NFT(t.asset_identifier, data.id, data.nft, string.Empty, string.Empty));
                    }
                }
                return nfts;
            }
        }

        private class FTMetaDataCache
        {
            private readonly Blockchain _chain;

            private readonly CachedDictionaryAsync<string, FungibleTokenData> _tokenId2Data;

            public FTMetaDataCache(Blockchain chain)
            {
                _chain = chain;
                _tokenId2Data = new CachedDictionaryAsync<string, FungibleTokenData>(RetrieveMetaData);
            }

            public ValueTask<FungibleTokenData> Get(string tokenId) => _tokenId2Data.Get(tokenId);

            private async Task<FungibleTokenData> RetrieveMetaData(string id, object passedInfo)
            {
                var parsed = Address.ParseFromFullTokenId(id);
                var metaData = await FungibleTokenMetaData.ForTokenContract(_chain, parsed.address + "." + parsed.contract).ConfigureAwait();
                return new FungibleTokenData(id, metaData);
            }
        }
    }
}
