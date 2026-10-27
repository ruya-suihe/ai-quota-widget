using System;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

internal static class ClientCrypto {
    [StructLayout(LayoutKind.Sequential)] struct AuthInfo {
        internal int Size,Version;
        internal IntPtr Nonce;internal int NonceSize;
        internal IntPtr Aad;internal int AadSize;
        internal IntPtr Tag;internal int TagSize;
        internal IntPtr Mac;internal int MacSize,AadCount;
        internal long DataCount;internal int Flags;
    }
    internal static byte[] UnwrapKey(string encoded) {
        var bytes=Convert.FromBase64String(encoded);
        if(bytes.Length<6 || Encoding.ASCII.GetString(bytes,0,5)!="DPAPI")throw new CryptographicException("Unsupported client key protection.");
        var wrapped=new byte[bytes.Length-5];Buffer.BlockCopy(bytes,5,wrapped,0,wrapped.Length);
        try {return ProtectedData.Unprotect(wrapped,null,DataProtectionScope.CurrentUser);}
        finally {Array.Clear(bytes,0,bytes.Length);Array.Clear(wrapped,0,wrapped.Length);}
    }
    internal static byte[] DecryptGcm(byte[] key,byte[] nonce,byte[] ciphertext,byte[] tag) {
        IntPtr algorithm=IntPtr.Zero,handle=IntPtr.Zero;
        GCHandle pinnedNonce=default(GCHandle),pinnedTag=default(GCHandle);
        var output=new byte[ciphertext.Length];bool success=false;
        try {
            Check(BCryptOpenAlgorithmProvider(out algorithm,"AES",null,0));
            byte[] mode=Encoding.Unicode.GetBytes("ChainingModeGCM\0");Check(BCryptSetProperty(algorithm,"ChainingMode",mode,mode.Length,0));
            Check(BCryptGenerateSymmetricKey(algorithm,out handle,IntPtr.Zero,0,key,key.Length,0));
            pinnedNonce=GCHandle.Alloc(nonce,GCHandleType.Pinned);pinnedTag=GCHandle.Alloc(tag,GCHandleType.Pinned);
            var auth=new AuthInfo {Size=Marshal.SizeOf(typeof(AuthInfo)),Version=1,Nonce=pinnedNonce.AddrOfPinnedObject(),NonceSize=nonce.Length,Tag=pinnedTag.AddrOfPinnedObject(),TagSize=tag.Length};
            int written;Check(BCryptDecrypt(handle,ciphertext,ciphertext.Length,ref auth,IntPtr.Zero,0,output,output.Length,out written,0));
            if(written!=output.Length)throw new CryptographicException("Invalid client cipher length.");
            success=true;return output;
        } finally {
            if(!success)Array.Clear(output,0,output.Length);
            if(pinnedNonce.IsAllocated)pinnedNonce.Free();if(pinnedTag.IsAllocated)pinnedTag.Free();
            if(handle!=IntPtr.Zero)BCryptDestroyKey(handle);if(algorithm!=IntPtr.Zero)BCryptCloseAlgorithmProvider(algorithm,0);
        }
    }
    internal static byte[] DecryptCookie(byte[] encrypted,byte[] key) {
        if(encrypted.Length>=3 && encrypted[0]=='v' && encrypted[1]=='1' && (encrypted[2]=='0' || encrypted[2]=='1')) {
            if(key==null || encrypted.Length<31)throw new CryptographicException("Client cookie key unavailable.");
            var nonce=new byte[12];var tag=new byte[16];var body=new byte[encrypted.Length-31];
            Buffer.BlockCopy(encrypted,3,nonce,0,12);Buffer.BlockCopy(encrypted,15,body,0,body.Length);Buffer.BlockCopy(encrypted,encrypted.Length-16,tag,0,16);
            return DecryptGcm(key,nonce,body,tag);
        }
        if(encrypted.Length>=3 && encrypted[0]=='v')throw new CryptographicException("Unsupported client cookie protection.");
        return ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser);
    }
    static void Check(int status){if(status!=0)throw new CryptographicException("Client cookie authentication failed.");}
    [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm,string id,string provider,int flags);
    [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptSetProperty(IntPtr handle,string property,byte[] input,int length,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptGenerateSymmetricKey(IntPtr algorithm,out IntPtr key,IntPtr keyObject,int objectSize,byte[] secret,int secretSize,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptDecrypt(IntPtr key,byte[] input,int length,ref AuthInfo auth,IntPtr iv,int ivSize,byte[] output,int outputSize,out int result,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptDestroyKey(IntPtr key);
    [DllImport("bcrypt.dll")] static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm,int flags);
}
