using StacksForce.Utils;
using System.Threading.Tasks;

namespace StacksForce.Dependencies
{
    public interface IBIP39
    {
        string? MnemonicToSeedHex(string mnemonic, string password);
        string GenerateMnemonicPhrase();
    }

    public interface IHDKeyProvider
    {
        IHDKey GetFromSeed(string seed);
    }

    public interface IHDKey
    {
        IHDKey Derive(string path);
        IHDKey Derive(uint index);
        byte[] PublicKey { get; }
        byte[] PrivateKey { get; }
        string ExtendedPrivateKey { get; }
    }

    public interface IHttpClient
    {
        Task<AsyncCallResult<string>> Get(string uri);
        Task<AsyncCallResult<string>> PostBinary(string uri, byte[] bytes);
        Task<AsyncCallResult<string>> PostJson(string uri, object json);
    }

    public interface ICryptography
    {
        byte[] Sha256(byte[] data);
        byte[] Sha512_256(byte[] data);
        byte[] RipeMD160(byte[] data);

        byte[] Secp256k1Sign(byte[] data, byte[] privateKey, out int recoveryId);
        byte[] Secp256k1GetPublicKey(byte[] privateKey, bool compressed);
    }
}
