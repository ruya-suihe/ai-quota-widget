using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Native Windows SQLite, read-only client connections. No downloaded driver,
// database copies, browser discovery, or shell execution.
internal static class ClientSqlite {
    internal sealed class ReadAccess:IDisposable {
        internal readonly string Url;
        readonly List<FileStream> pins=new List<FileStream>();
        internal ReadAccess(string path) {
            try {
                if(File.Exists(path+"-wal") && !File.Exists(path+"-shm"))throw new IOException("Client database is not available for read-only access.");
                bool immutable=false;
                if(File.Exists(path+"-wal")) {
                    // Keep existing main/WAL/SHM names until SQLite closes, even
                    // if the last client closes concurrently. Writers remain allowed.
                    foreach(string file in new[]{path,path+"-wal",path+"-shm"})pins.Add(new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite));
                } else {
                    // Block existing/new writers and deletion/replacement before
                    // promising immutable. Never use it on a mutable live database.
                    pins.Add(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
                    if(File.Exists(path+"-wal"))throw new IOException("Client database changed while opening.");
                    immutable=true;
                }
                Url=new Uri(Path.GetFullPath(path)).AbsoluteUri+"?mode=ro"+(immutable?"&immutable=1":"");
            }catch{Dispose();throw;}
        }
        public void Dispose(){foreach(var pin in pins)pin.Dispose();pins.Clear();}
    }
    internal static List<object[]> Read(string path,string sql) {
        if(!File.Exists(path))return new List<object[]>();
        IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
        ReadAccess access=null;
        try {
            access=new ReadAccess(path);
            if(sqlite3_open_v2(Utf8(access.Url),out db,1|0x40,IntPtr.Zero)!=0)throw new IOException("Client database cannot be read.");
            sqlite3_busy_timeout(db,500);
            if(sqlite3_prepare_v2(db,Utf8(sql),-1,out statement,IntPtr.Zero)!=0 || sqlite3_stmt_readonly(statement)!=1)throw new IOException("Client database schema is unavailable.");
            var rows=new List<object[]>();int code;
            while((code=sqlite3_step(statement))==100) {
                if(rows.Count>=64)throw new IOException("Client database returned too many login records.");
                var row=new object[sqlite3_column_count(statement)];
                for(int i=0;i<row.Length;i++) {
                    int kind=sqlite3_column_type(statement,i);
                    if(kind==5)continue;
                    if(kind==1)row[i]=sqlite3_column_int64(statement,i);
                    else {
                        int size=sqlite3_column_bytes(statement,i);if(size>1048576)throw new IOException("Client record is too large.");
                        var data=new byte[size];if(size>0)Marshal.Copy(sqlite3_column_blob(statement,i),data,0,size);
                        row[i]=kind==3 ? (object)Encoding.UTF8.GetString(data) : data;
                    }
                }
                rows.Add(row);
            }
            if(code!=101)throw new IOException("Client database is busy or unreadable.");
            return rows;
        } finally {if(statement!=IntPtr.Zero)sqlite3_finalize(statement);if(db!=IntPtr.Zero)sqlite3_close(db);if(access!=null)access.Dispose();}
    }
    static byte[] Utf8(string text){return Encoding.UTF8.GetBytes(text+"\0");}
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] name,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_stmt_readonly(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_type(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
}
