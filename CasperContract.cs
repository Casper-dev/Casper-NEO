using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;
using System.IO;

public class CasperContract: SmartContract
{
    //enum Role : byte {Normal=1, Banned};
    public const byte RoleNormal = 0x01;
    public const byte RoleBanned = 0x02;

    public const byte MaxFailedPings = 5;

    public const ulong bytesPerToken = 128;

    // at nodeID + *Sx we store provider parameters
    public const string sizeSx     = ":size";
    public const string freeSx     = ":free";
    public const string ipPortSx   = ":ipport";
    public const string thriftSx   = ":thrift";
    public const string telegramSx = ":telegram";
    public const string roleSx     = ":role";
    public const string fpingSx    = ":fping";
    public const string indexSx    = ":index";
    public const string ownerSx    = ":owner";

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

        return new object[]{};
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

        //Map<string, string> nodeMap = new Map<string, string>();
        //nodeMap[nodeID] = "kek";
        //Runtime.Notify("stored:", nodeMap[nodeID]);

        // TODO check balance

        byte[] v = Storage.Get(Storage.CurrentContext, nodeID + sizeSx);
        if (v != null)
            Fatal("provider already registered");

        //var node = (NodeHash: nodeID, Telegram: telegram, UDPIpPort: udpIpPort, IPPort: ipPort, Size: size);
        BigInteger count;
        v = Storage.Get(Storage.CurrentContext, nodeS);
        count = (v == null) ? 1 : v.AsBigInteger();
        Runtime.Notify("count:", count);
        Storage.Put(Storage.CurrentContext, nodeS + count, nodeID);

        BigInteger zero = 0;
        byte[] free = size.AsByteArray();
        Storage.Put(Storage.CurrentContext, nodeID + sizeSx, free);
        Storage.Put(Storage.CurrentContext, nodeID + freeSx, free);
        Storage.Put(Storage.CurrentContext, nodeID + ipPortSx, ipPort);
        Storage.Put(Storage.CurrentContext, nodeID + thriftSx, thrift);
        Storage.Put(Storage.CurrentContext, nodeID + telegramSx, telegram);
        Storage.Put(Storage.CurrentContext, nodeID + roleSx, role);
        Storage.Put(Storage.CurrentContext, nodeID + fpingSx, zero.AsByteArray());
        Storage.Put(Storage.CurrentContext, nodeID + indexSx, count.AsByteArray());
        Storage.Put(Storage.CurrentContext, nodeID + ownerSx, ExecutionEngine.CallingScriptHash);

        count = count + 1;
        Storage.Put(Storage.CurrentContext, nodeS, count.AsByteArray());

        return new object[]{size};
    }

    public static object[] GetNodeInfo(object[] args)
    {
        var nodeID = (string)args[0];
        Runtime.Notify("exec: getnodeinfo", nodeID);
        byte[] sz = Storage.Get(Storage.CurrentContext, nodeID + sizeSx);
        if (sz == null)
            Fatal("absent value");

        byte[] free   = Storage.Get(Storage.CurrentContext, nodeID + freeSx);
        byte[] ipPort = Storage.Get(Storage.CurrentContext, nodeID + ipPortSx);
        byte[] thrift = Storage.Get(Storage.CurrentContext, nodeID + thriftSx);
        byte[] role   = Storage.Get(Storage.CurrentContext, nodeID + roleSx);

        return new object[]{sz, free, ipPort, thrift, role[0]};
    }

    public static object[] UpdateIpPort(object[] args)
    {
        var nodeID = (string)args[0];
        var ipPort = (string)args[1];

        var owner = Storage.Get(Storage.CurrentContext, nodeID + ownerSx);
        if (!slicesEqual(owner, ExecutionEngine.CallingScriptHash))
            Fatal("wrong address");

        Storage.Put(Storage.CurrentContext, nodeID + ipPortSx, ipPort);

        return new object[]{ipPort};
    }

    public static object[] ConfirmUpload(object[] args)
    {
        var nodeID      = (string)args[0];
        var fileID      = (string)args[1];
        BigInteger size = (long)  args[2];

        byte[] freeVal = Storage.Get(Storage.CurrentContext, nodeID + freeSx);
        BigInteger free = freeVal.AsBigInteger();
        if (free < size)
            Fatal("insufficient space");

        free = free - size;
        Storage.Put(Storage.CurrentContext, nodeID + freeSx, free.AsByteArray());

        string fp = fileS + fileID;
        Storage.Put(Storage.CurrentContext, fp + sizeSx, size.AsByteArray());

        return new object[]{free};
    }

    public static object[] ConfirmUpdate(object[] args)
    {
        var nodeID      = (string)args[0];
        var fileID      = (string)args[1];
        BigInteger size = (long)args[2];

        byte[] freeVal = Storage.Get(Storage.CurrentContext, nodeID + freeSx);
        BigInteger free = freeVal.AsBigInteger();

        string fp = fileS + fileID;
        byte[] curVal = Storage.Get(Storage.CurrentContext, fp + sizeSx);
        if (curVal == null)
            Fatal("no file with such id");

        BigInteger csize = curVal.AsBigInteger();
        if (free + csize < size)
            Fatal("insufficient space");

        free = free + csize - size;
        Storage.Put(Storage.CurrentContext, nodeID + freeSx, free.AsByteArray());
        Storage.Put(Storage.CurrentContext, fp + sizeSx, size.AsByteArray());

        return new object[]{free};
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
            byte[] free = Storage.Get(Storage.CurrentContext, nodeID + freeSx);
            if (size < free.AsBigInteger())
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
        var v = Storage.Get(Storage.CurrentContext, nodeID + fpingSx);
        var fp = v.AsBigInteger();
        fp = fp + 1;

        // 2. ban node if failedPings > 5
        // 3. return true if node is banned
        if (fp > MaxFailedPings)
        {
            Storage.Put(Storage.CurrentContext, nodeID + roleSx, RoleBanned);
            return new object[]{true};
        }

        return new object[]{false};
    }

    // FIXME NotifySpaceFreed in solidity
    public static void NotifyDelete(object[] args)
    {
        var nodeID      = (string)args[0];
        var fileID      = (string)args[1];
        BigInteger size = (long)args[2];

        var fv = Storage.Get(Storage.CurrentContext, nodeID + freeSx);
        if (fv == null)
            Fatal("no free space");

        BigInteger free = fv.AsBigInteger();
        // TODO take size from Storage by fileID?
        free = free + size;
        Storage.Put(Storage.CurrentContext, nodeID + freeSx, free.AsByteArray());
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
            byte[] size = Storage.Get(Storage.CurrentContext, nodeID + sizeSx);
            Runtime.Notify("node:", nodeID, size);
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
