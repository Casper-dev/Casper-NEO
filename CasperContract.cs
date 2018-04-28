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
        public byte[] ipPort, thrift, telegram, role, owner;
    }

    private static byte[] NSerialize(Node n)
    {
        var a = new object[]{
            n.size, n.free, n.index, n.fping,
            n.ipPort, n.thrift, n.telegram, n.role, n.owner,
        };
        return a.Serialize();
    }

    private static Node NDeserialize(byte[] b)
    {
        var a = (object[])b.Deserialize();
        Node n = new Node();
        n.size = (BigInteger)a[0];
        n.free = (BigInteger)a[1];
        n.index = (BigInteger)a[2];
        n.fping = (BigInteger)a[3];
        n.ipPort = (byte[])a[4];
        n.thrift = (byte[])a[5];
        n.telegram = (byte[])a[6];
        n.role = (byte[])a[7];
        n.owner = (byte[])a[8];

        return n;
    }

    //enum Role : byte {Normal=1, Banned};
    public const byte RoleNormal = 0x01;
    public const byte RoleBanned = 0x02;

    public const byte MaxFailedPings = 5;

    public const ulong bytesPerToken = 128;

    // at nodeID + *Sx we store provider parameters
    public const string sizeSx = ":size";

    // at NodeS we store counter with #number of nodes + 1
    // at NodeS + "i", i = 1.. we store node hashes
    public const string nodeS = "nodes";
    public const string fileS = "files";

    // events
    public const string ConsensusResultEvent = "consensus";
    public const string VerificationTargetEvent = "verification";

    public static object[] Main(string op, params object[] args)
    {
        Runtime.Notify(op, args);
        if (op == "register")
            return RegisterProvider(args);
        else if (op == "getinfo")
            return GetNodeInfo(args);
        else if (op == "getpeers")
            return GetPeers(args);
        else if (op == "getpingtarget")
            return GetPingTarget(args);
        else if (op == "getfilesize")
            return GetFileSize(args);
        else if (op == "setipport")
            return UpdateIpPort(args);
        else if (op == "confirmupload")
            return ConfirmUpload(args);
        else if (op == "confirmupdate")
            return ConfirmUpdate(args);
        else if (op == "notifydelete")
            NotifyDelete(args);
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
        var ipPort      = (byte[])args[2];
        var thrift      = (byte[])args[3];
        var telegram    = (byte[])args[4];
        byte[] role     = new byte[]{RoleNormal};
        Runtime.Notify("exec: registerprovider", nodeID, size, ipPort, thrift, role);

        // TODO check balance
        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node != null)
            Fatal("provider already registered");

        //var node = (NodeHash: nodeID, Telegram: telegram, UDPIpPort: udpIpPort, IPPort: ipPort, Size: size);
        BigInteger count;
        var v = Storage.Get(Storage.CurrentContext, nodeS);
        count = (v == null) ? 1 : v.AsBigInteger();
        Runtime.Notify("count:", count);
        Storage.Put(Storage.CurrentContext, nodeS + count, nodeID);

        Node n = new Node();
        n.size     = size;
        n.free     = size;
        n.ipPort   = ipPort;
        n.thrift   = thrift;
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

        return new object[]{n.size, n.free, n.ipPort, n.thrift, n.role[0]};
    }

    public static object[] UpdateIpPort(object[] args)
    {
        var nodeID = (byte[])args[0];
        var ipPort = (byte[])args[1];

        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");

        Node n = NDeserialize(node);
        if (!slicesEqual(n.owner, ExecutionEngine.CallingScriptHash))
            Fatal("wrong address");

        n.ipPort = ipPort;
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));

        return new object[]{ipPort};
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
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));
        Storage.Put(Storage.CurrentContext, fileS + fileID, size.AsByteArray());

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

        byte[] curVal = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (curVal == null)
            Fatal("no file with such id");

        BigInteger csize = curVal.AsBigInteger();
        if (n.free + csize < size)
            Fatal("insufficient space");

        n.free = n.free + csize - size;
        Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));
        Storage.Put(Storage.CurrentContext, fileS + fileID, size.AsByteArray());

        return new object[]{n.free};
    }

    // TODO get random peers
    public static object[] GetPeers(object[] args)
    {
        long size = (long)args[0];

        // FIXME use empty slice instead of nil because of more nice output
        // in neo-python
        byte[] nil = new byte[0]{};
        object[] ips = new byte[4][]{nil, nil, nil, nil};
        byte[] v = Storage.Get(Storage.CurrentContext, nodeS);
        BigInteger count = v.AsBigInteger();

        int ind = 0;
        for(long i = 1; i < count; i++)
        {
            byte[] nodeID = Storage.Get(Storage.CurrentContext, nodeS + i);
            var node = Storage.Get(Storage.CurrentContext, nodeID);
            Node n = NDeserialize(node);
            if (size < n.free)
            {
                ips[ind] = nodeID;
                ind = ind + 1;
                if (ind == 3)
                    break;
            }
        }
        return ips;
    }

    public static object[] GetPingTarget(object[] args)
    {
        var cv = Storage.Get(Storage.CurrentContext, nodeS);
        var count = cv.AsBigInteger();
        var nonce = (byte[])args[0];

        Header header = Blockchain.GetHeader(Blockchain.GetHeight());
        var rand1 = header.Hash.AsBigInteger();
        var rand2 = nonce.AsBigInteger();
        var rand = rand1 + rand2;
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

        if (alive)
            return new object[]{false};

        // 1. increase number of failed pings
        var node = Storage.Get(Storage.CurrentContext, nodeID);
        if (node == null)
            Fatal("absent value");
        Node n = NDeserialize(node);
        n.fping = n.fping + 1;

        // 2. ban node if failedPings > 5
        // 3. return true if node is banned
        if (n.fping > MaxFailedPings)
        {
            n.role = new byte[]{RoleBanned};
            Storage.Put(Storage.CurrentContext, nodeID, NSerialize(n));
            return new object[]{true};
        }

        return new object[]{false};
    }

    public static object[] GetFileSize(object[] args)
    {
        var fileID = (string)args[0];
        var val = Storage.Get(Storage.CurrentContext, fileS + fileID);
        if (val == null)
            Fatal("no file with such id");

        BigInteger size = val.AsBigInteger();
        return new object[]{size};
    }

    // FIXME NotifySpaceFreed in solidity
    public static void NotifyDelete(object[] args)
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
    }

    public static void NotifyVerificationTarget(object[] args)
    {
        var nodeID = (string)args[0];
        var fileID = (string)args[1];
        ThrowEvent(VerificationTargetEvent, nodeID, fileID);
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
