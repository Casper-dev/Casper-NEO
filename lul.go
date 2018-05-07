package main

import (
	"context"
	"fmt"

	"bytes"
	"encoding/hex"
	"encoding/json"
	"net/http"
	"net/url"
	"sort"

	"github.com/CityOfZion/neo-go/pkg/core/transaction"
	"github.com/CityOfZion/neo-go/pkg/crypto"
	"github.com/CityOfZion/neo-go/pkg/rpc"
	"github.com/CityOfZion/neo-go/pkg/smartcontract"
	"github.com/CityOfZion/neo-go/pkg/util"
	"github.com/CityOfZion/neo-go/pkg/wallet"
	"github.com/davecgh/go-spew/spew"
)

const (
	gasAssetID      = "602c79718b16e442de58778e148d0b1084e3b2dffd5de6b7b16cee7969282de7"
	wifKey          = "KxDgvEKzgSBPPfuVfw67oPQBSjidEiqTHURKSDL1R7yGaGYAeYnr"
	testscript      = "dc7e6a0e779a86f66f7f3b3088b1c76e285835cd"
	defaultAddress  = "AK2nJJpJr6o664CWJKi1QRXjqeic2zRp8y"
	defaultEndpoint = "http://127.0.0.1:10332"
)

func main() {
	wif, err := wallet.WIFDecode(wifKey, wallet.WIFVersion)
	if err != nil {
		fmt.Println(err)
		return
	}

	//tran := &transaction.Transaction{}
	//tstr, err := hex.DecodeString(
	//	"d1013101140566696c6531036c756c53c10d636f6e6669726d75706c6f616467cd3558286ec7b188303b7f6ff6869a770e6a7edc0000000000000000012023ba2703c53263e8d6e522dc32203339dcd8eee901e58b25af169798844e8ea148960994d9a1c71a7b8f27ae24638eb254707f11a5000001e72d286979ee6cb1b7e65dfddfb2e384100b8d148e7758de42e4168b71792c60c081f319a600000023ba2703c53263e8d6e522dc32203339dcd8eee9014140c6de83f81e815fa492fa8359d6be9c56829af9b2536149493d202c46684bf40f2a820c53e651723d7493451d8021de61a199ebf2b00c193a62dba5800a19adb12321031a6c6fbbdf02ca351745fa86b9ba5a9452d785ac4f7fc2b7548ca2a46c4fcf4aac",
	//)
	//err = tran.DecodeBinary(bytes.NewReader(tstr))
	//fmt.Println(err)
	//spew.Dump(tran)
	//return

	c, err := rpc.NewClient(context.TODO(), defaultEndpoint, rpc.ClientOptions{})
	if err != nil {
		fmt.Println(err)
		return
	}
	res, err := c.InvokeFunction(testscript, "confirmupload", []smartcontract.Parameter{
		{smartcontract.StringType, "lul"},
		{smartcontract.StringType, "file1"},
		{smartcontract.IntegerType, 20},
	})

	//spew.Dump(res)
	fmt.Printf("%+v %v\n", res.Result.Script, err)

	script, err := hex.DecodeString(res.Result.Script)
	t := transaction.NewInvocationTX(script)

	fmt.Println("asset", gasAssetID)
	assetID, err := util.Uint256DecodeString(gasAssetID)
	fmt.Println(assetID.String(), err)

	//amount, err := util.Fixed8DecodeString(res.Result.GasConsumed)
	amount, err := util.Fixed8DecodeString("0.0001")
	fmt.Println(amount.String(), err)

	addr, err := wif.PrivateKey.Address()
	if err != nil {
		fmt.Println(err)
		return
	}

	b, err := getBalance(addr)
	if err != nil {
		fmt.Println(err)
		return
	}
	inputs, spent := calculateInputs("", b, amount)
	spew.Dump(inputs)
	spew.Dump(spent)

	t.Inputs = inputs

	p, _ := wif.PrivateKey.Address()
	//export const getScriptHashFromAddress = (address) => {
	//	let hash = ab2hexstring(base58.decode(address))
	//	return reverseHex(hash.substr(2, 40))
	//  }
	bs, _ := crypto.Base58Decode(p)
	fmt.Println(len(bs))
	l := 20
	rev := make([]byte, l)
	for i := 0; i < l; i++ {
		rev[i] = bs[l-i]
	}
	hash := hex.EncodeToString(bs[1:21])
	a, err := util.Uint160DecodeString(hash)
	if err != nil {
		fmt.Println(err)
		return
	}

	t.Attributes = []*transaction.Attribute{{Data: bs[1:21], Usage: transaction.Script}}

	t.AddOutput(&transaction.Output{assetID, spent - amount, a})

	t.Hash()

	buf := &bytes.Buffer{}
	err = t.EncodeBinary(buf)
	if err != nil {
		fmt.Println(err)
		return
	}

	signature, err := wif.PrivateKey.Sign(buf.Bytes())
	if err != nil {
		fmt.Println(err)
		return
	}

	invocS, _ := hex.DecodeString("40" + hex.EncodeToString(signature))
	fmt.Printf("invoc: %x\n", invocS)

	pubkey, _ := wif.PrivateKey.PublicKey()
	fmt.Printf("pubkey: %x\n", pubkey)

	verifS, _ := hex.DecodeString("21" + hex.EncodeToString(pubkey) + "ac")
	//verifS, err := wif.PrivateKey.Signature()
	//if err != nil {
	//    fmt.Println(err)
	//    return
	//}
	fmt.Printf("verif: %x\n", verifS)

	t.Scripts = []*transaction.Witness{{invocS, verifS}}

	fmt.Println(t.Hash())
	spew.Dump(t)

	buf = &bytes.Buffer{}
	err = t.EncodeBinary(buf)
	if err != nil {
		fmt.Println(err)
		return
	}

	rawTx := buf.Bytes()
	resp, err := c.SendRawTransaction(hex.EncodeToString(rawTx))
	fmt.Println(resp, err)

}

// UTXO represents unspent transaction output
type UTXO struct {
	Index uint16       `json:"index"`
	TXID  util.Uint256 `json:"txid"`
	Value util.Fixed8  `json:"value"`
}

// AssetInfo represents state of particular asset
type AssetInfo struct {
	Balance util.Fixed8 `json:"balance"`
	Unspent []UTXO      `json:"unspent"`
}

// Balance represents state of the wallet
type Balance struct {
	GAS     AssetInfo `json:"GAS,omitempty"`
	NEO     AssetInfo `json:"NEO,omitempty"`
	Address string    `json:"address"`
	Net     string    `json:"net"`
}

func getBalance(address string) (*Balance, error) {
	apiURL := &url.URL{Scheme: "http", Host: "127.0.0.1:5000", Path: "/v2/address/balance"}
	req, err := http.NewRequest("GET", fmt.Sprintf("%s/%s", apiURL.String(), address), nil)
	if err != nil {
		return nil, err
	}
	//req.Header.Set("Accept", "application/json-rpc")
	req.Header.Set("Accept", "application/json")

	c := http.Client{}
	resp, err := c.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var balance Balance
	err = json.NewDecoder(resp.Body).Decode(&balance)

	spew.Dump(balance)
	return &balance, err
}

func calculateInputs(pubkey string, balances *Balance, gasCost util.Fixed8) ([]*transaction.Input, util.Fixed8) {
	// TODO add 'intents' argument
	required := gasCost
	//assets := map[string]util.Fixed8{gasAssetID: gasCost}
	sort.Slice(balances.GAS.Unspent, func(i, j int) bool {
		return balances.GAS.Unspent[i].Value > balances.GAS.Unspent[j].Value
	})

	selected := util.Fixed8(0)
	num := uint16(0)
	for _, us := range balances.GAS.Unspent {
		if selected >= required {
			break
		}
		selected += us.Value
		num++
	}
	if selected < required {
		spew.Dump(balances)
		panic("lt")
	}
	fmt.Printf("Selected balances: %s\n", selected)

	inputs := make([]*transaction.Input, num)
	for i := uint16(0); i < num; i++ {
		inputs[i] = &transaction.Input{
			PrevHash:  balances.GAS.Unspent[i].TXID,
			PrevIndex: balances.GAS.Unspent[i].Index,
		}
	}
	return inputs, selected
}
