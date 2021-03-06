using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;
using System.IO;

public class CasperContract: SmartContract
{
    // Order in which fields are specified is used
    // in serialization/deserialization procedures.
    // If you change anything here, change them accordingly.
    private struct Node
    {
        public BigInteger size, free, index, fping;
        public uint lastFail;
        public byte role;
        public byte[] apiAddr, rpcAddr, telegram, owner;
    }

    private static byte[] NSerialize(Node n)
    {
        var a = new object[]{
            n.size, n.free, n.index, n.fping,
            n.lastFail,
            n.role,
            n.apiAddr, n.rpcAddr, n.telegram, n.owner,
        };
        return a.Serialize();
    }

    private static Node NDeserialize(byte[] b)
    {
        var a = (object[])b.Deserialize();
        Node n = new Node();
        n.size     = (BigInteger)a[0];
        n.free     = (BigInteger)a[1];
        n.index    = (BigInteger)a[2];
        n.fping    = (BigInteger)a[3];
        n.lastFail = (uint)a[5];
        n.role     = (byte)a[7];
        n.apiAddr  = (byte[])a[4];
        n.rpcAddr  = (byte[])a[5];
        n.telegram = (byte[])a[6];
        n.owner    = (byte[])a[8];

        return n;
    }

    private struct File
    {
        public BigInteger size;
        public byte[] node1, node2, node3, node4;
    }

    private static byte[] FSerialize(File f)
    {
        var a = new object[]{f.size, f.node1, f.node2, f.node3, f.node4};
        return a.Serialize();
    }

    private static File FDeserialize(byte[] b)
    {
        var a = (object[])b.Deserialize();
        File f = new File();
        f.size  = (BigInteger)a[0];
        f.node1 = (byte[])a[1];
        f.node2 = (byte[])a[2];
        f.node3 = (byte[])a[3];
        f.node4 = (byte[])a[4];

        return f;
    }

    //enum Role : byte {Normal=1, Banned};
    public const byte RoleNormal = 0x01;
    public const byte RoleBanned = 0x02;

    public const uint PingSuspectInterval = 3600;
    public const byte MaxFailedPings = 5;
    public const ulong bytesPerToken = 128;

    // at nodeS we store counter with #number of nodes + 1
    // at nodeS + "i", i = 1.. we store node hashes
    public const string nodeS = "nodes";

    // at nodeF + "nodeID" we store list of file hashes
    // for the specified node
    public const string nodeF = "nodef:";
    public const string fileS = "files:";

    // events
    public const string ConsensusResultEvent = "consensus";
    public const string VerificationTargetEvent = "verification";

    public static object[] Main(string op, params object[] args)
    {
        Runtime.Notify(op, args);
        if (op == "register")
            return RegisterProvider(args);
        else if (op == "addtoken")
            return new object[]{};
        else if (op == "confirmdownload")
            return new object[]{};
        else if (op == "confirmupload")
            return ConfirmUpload(args);
        else if (op == "confirmupdate")
            return ConfirmUpdate(args);
        else if (op == "getfile")
            return GetFile(args);
        else if (op == "getfilesize")
            return GetFileSize(args);
        else if (op == "getfilesnumber")
            return GetFilesNumber(args);
        else if (op == "getinfo")
            return GetNodeInfo(args);
        else if (op == "getpeers")
            return GetPeers(args);
        else if (op == "getstoringpeers")
            return GetStoringPeers(args);
        else if (op == "getpingtarget")
            return GetPingTarget(args);
        else if (op == "notifydelete")
            return new object[]{};
        else if (op == "notifyspacefreed")
            return NotifySpaceFreed(args);
        else if (op == "notifyverificationtarget")
            return NotifyVerificationTarget(args);
        else if (op == "prepay")
            return new object[]{};
        else if (op == "sendpingresult")
            return SendPingResult(args);
        else if (op == "updateipport")
            return UpdateIpPort(args);
        else if (op == "verifyreplication")
            return new object[]{};
        else if (op == "debugprint")
            DebugPrintNodes();
        else if (op == "debugclear")
            DebugClear();
        else
            Fatal("unknown operation");

        return new object[]{"ok"};
    }

    public static object[] RegisterProvider(object[] args)
    {
        var nodeID      = (byte[])args[0];
        BigInteger size = (long)  args[1];
        var apiAddr     = (byte[])args[2];
        var rpcAddr     = (byte[])args[3];
        var telegram    = (byte[])args[4];
        byte role       = RoleNormal;
        Runtime.Notify("exec: registerprovider", nodeID, size, apiAddr, rpcAddr, role);

        // TODO check balance
        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node != null)
            Fatal("provider already registered");

        BigInteger count;
        var v = Storage.Get(Storage.CurrentContext, nodeS);
        count = (v == null) ? 1 : v.AsBigInteger();
        Runtime.Notify("count:", count);
        Storage.Put(Storage.CurrentContext, nodeS + count, nodeID);

        Node n = new Node();
        n.size     = size;
        n.free     = size;
        n.apiAddr  = apiAddr;
        n.rpcAddr  = rpcAddr;
        n.telegram = telegram;
        n.role     = role;
        n.index    = count;
        n.fping    = 0;
        n.owner    = ExecutionEngine.CallingScriptHash;

        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        count = count + 1;
        Storage.Put(Storage.CurrentContext, nodeS, count.AsByteArray());

        return new object[]{size};
    }

    public static object[] GetNodeInfo(object[] args)
    {
        var nodeID = (string)args[0];
        Runtime.Notify("exec: getnodeinfo", nodeID);
        byte[] node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");

        Node n = NDeserialize(node);

        return new object[]{n.size, n.free, n.apiAddr, n.rpcAddr, n.role};
    }

    public static object[] UpdateIpPort(object[] args)
    {
        var nodeID  = (byte[])args[0];
        var apiAddr = (byte[])args[1];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");

        Node n = NDeserialize(node);
        if (!slicesEqual(n.owner, ExecutionEngine.CallingScriptHash))
            Fatal("wrong address");

        n.apiAddr = apiAddr;
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        return new object[]{apiAddr};
    }

    public static object[] ConfirmUpload(object[] args)
    {
        var nodeID      = (string)args[0];
        var fileID      = (string)args[1];
        BigInteger size = (long)  args[2];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");

        Node n = NDeserialize(node);
        if (n.free < size)
            Fatal("insufficient space");

        n.free = n.free - size;

        var fp = nodeF + nodeID;
        var countRaw = Storage.Get(Storage.CurrentContext, fp);
        BigInteger count = countRaw.AsBigInteger();
        count = count + 1;
        Storage.Put(Storage.CurrentContext, fp, count.AsByteArray());
        Storage.Put(Storage.CurrentContext, fp + count, fileID);

        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        File file;
        var fileRaw = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (fileRaw == null)
            file = new File();
        else
            file = FDeserialize(fileRaw);

        byte[] id = (byte[])args[0];
        if (file.node1 == null || file.node1.Length == 0)
            file.node1 = id;
        else if (file.node2 == null || file.node2.Length == 0)
            file.node2 = id;
        else if (file.node3 == null || file.node3.Length == 0)
            file.node3 = id;
        else if (file.node4 == null || file.node4.Length == 0)
            file.node4 = id;

        Storage.Put(Storage.CurrentContext, fileS + fileID, FSerialize(file));

        return new object[]{n.free};
    }

    public static object[] ConfirmUpdate(object[] args)
    {
        var nodeID      = (byte[])args[0];
        var fileID      = (byte[])args[1];
        BigInteger size = (long)args[2];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
          Fatal("absent value");

        Node n = NDeserialize(node);

        byte[] fileRaw = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (fileRaw == null)
            Fatal("no file with such id");

        File file = FDeserialize(fileRaw);
        BigInteger csize = file.size;
        if (n.free + csize < size)
            Fatal("insufficient space");

        n.free = n.free + csize - size;
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        file.size = size;
        Storage.Put(Storage.CurrentContext, fileS + fileID, FSerialize(file));

        return new object[]{n.free};
    }

    public static object[] GetFile(object[] args)
    {
        var nodeID = (string)args[0];
        var num    = (long)  args[1];

        var fp = nodeF + nodeID;
        var hash = Storage.Get(Storage.CurrentContext, fp + num);
        var fileRaw = Storage.Get(Storage.CurrentContext, fileS + hash);
        File file = FDeserialize(fileRaw);

        return new object[]{hash, file.size};
    }

    public static object[] GetFilesNumber(object[] args)
    {
        var nodeID = (string)args[0];
        var countRaw = Storage.Get(Storage.CurrentContext, nodeF + nodeID);
        BigInteger count = countRaw.AsBigInteger();
        return new object[]{count};
    }

    // TODO get random peers
    public static object[] GetPeers(object[] args)
    {
        long size  = (long)args[0];
        long count = (long)args[1];

        Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        BigInteger seed = header.ConsensusData;
        if (args.Length > 2) {
            seed = seed + (long)args[2];
        }

        // FIXME use empty slice instead of nil because of more nice output
        // in neo-python
        byte[] nil = new byte[0]{};
        object[] ips = new byte[4][]{nil, nil, nil, nil};
        byte[] v = Storage.Get(Storage.CurrentContext, nodeS);
        BigInteger amount = v.AsBigInteger();

        long ind = 0;
        for(long i = 1; i < amount; i++)
        {
            byte[] nodeID = Storage.Get(Storage.CurrentContext, nodeS + i);
            var node = Storage.Get(Storage.CurrentContext, nodeID);
            Node n = NDeserialize(node);
            if (size < n.free)
            {
                ips[ind] = nodeID;
                ind = ind + 1;
                if (ind == count)
                    break;
            }
        }
        return ips;
    }

    public static object[] GetStoringPeers(object[] args)
    {
        var fileID = (string)args[0];
        var fileRaw = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (fileRaw == null)
            return new object[]{"", "", "", ""};

        File file = FDeserialize(fileRaw);
        return new object[]{file.node1, file.node2, file.node3, file.node4};
    }

    public static object[] GetPingTarget(object[] args)
    {
        var cv = Storage.Get(Storage.CurrentContext, nodeS);
        var count = cv.AsBigInteger();
        var nonce = (byte[])args[0];

        Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        var rand1 = header.Hash.AsBigInteger();
        var rand2 = nonce.AsBigInteger();
        var rand  = rand1 + rand2;
        BigInteger nodeNum = 1 + rand % (count-1);

        Runtime.Notify("random", rand, rand1, rand2, nodeNum);

        var nodeID = Storage.Get(Storage.CurrentContext, nodeS + (long)nodeNum);
        Runtime.Notify("node id: ", nodeID);
        return new object[]{nodeID};
    }

    public static object[] SendPingResult(object[] args)
    {
        var nodeID = (string)args[0];
        var alive  = (bool)  args[1];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");

        Node n = NDeserialize(node);
        Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        if (alive)
        {
            if (header.Timestamp - n.lastFail < PingSuspectInterval)
                n.fping = n.fping - 1;
            else
                n.fping = 0;
        } else {
            if (n.fping != 0 && header.Timestamp - n.lastFail < PingSuspectInterval)
                n.fping = n.fping * 2;
            else
                n.fping = n.fping + 1;

            n.lastFail = header.Timestamp;
        }

        if (n.fping > MaxFailedPings)
            n.role = RoleBanned;

        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        bool banned = n.fping > 5;
        return new object[]{banned};
    }

    public static object[] GetFileSize(object[] args)
    {
        var fileID = (string)args[0];
        var fileRaw = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (fileRaw == null)
            Fatal("no file with such id");

        File file = FDeserialize(fileRaw);
        return new object[]{file.size};
    }

    public static object[] NotifySpaceFreed(object[] args)
    {
        var nodeID      = (string)args[0];
        var fileID      = (string)args[1];
        BigInteger size = (long)args[2];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");
        Node n = NDeserialize(node);

        // TODO take size from Storage by fileID?
        n.free = n.free + size;
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        return new object[]{};
    }

    public static object[] NotifyVerificationTarget(object[] args)
    {
        var nodeID = (string)args[0];
        var fileID = (string)args[1];
        ThrowEvent(VerificationTargetEvent, nodeID, fileID);
        return new object[]{};
    }

    public static object[] CheckVerification(object[] args)
    {
        var fileID = (string)args[0];
        ThrowEvent(ConsensusResultEvent, fileID, "", "", "", "");
        return new object[]{};
    }

    public static void ThrowEvent(string name, params object[] args) {
        Runtime.Notify(name, args);
    }

    public static void DebugPrintNodes()
    {
        byte[] cv = Storage.Get(Storage.CurrentContext, nodeS);
        BigInteger count = cv.AsBigInteger();

        for (long i = 1; i < count; i++)
        {
            byte[] nodeID = Storage.Get(Storage.CurrentContext, nodeS + i);
            var node = Storage.Get(Storage.CurrentContext, nodeID);
            Node n = NDeserialize(node);
            Runtime.Notify("node:", nodeID, n.size);
        }
    }

    public static void DebugClear()
    {
        // only clear counter
        byte[] cv = Storage.Get(Storage.CurrentContext, nodeS);
        BigInteger count = cv.AsBigInteger();
        count = 1;
        Storage.Put(Storage.CurrentContext, nodeS, count.AsByteArray());
    }

    private static bool slicesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;

        for(int i = 0; i < a.Length; i++)
           if (a[i] != b[i])
               return false;

        return true;
    }

    private static void Fatal(string message)
    {
        Runtime.Log("error: " + message);
        throw new System.Exception(message);
    }
}
