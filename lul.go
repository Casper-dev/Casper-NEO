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
	defaultEndpoint = "http://127.0.0.1:10332"
)

func main() {
	defer func() {
		if r := recover(); r != nil {
			fmt.Printf("error: %v", r)
		}
	}()

	wif, err := wallet.WIFDecode(wifKey, wallet.WIFVersion)
	if err != nil {
		panic(err)
	}
	c, err := rpc.NewClient(context.TODO(), defaultEndpoint, rpc.ClientOptions{})
	if err != nil {
		panic(err)
	}

	res, err := c.InvokeFunction(testscript, "confirmupload", []smartcontract.Parameter{
		{smartcontract.StringType, "lul"},
		{smartcontract.StringType, "file1"},
		{smartcontract.IntegerType, 20},
	})

	fmt.Printf("%+v %v\n", res.Result.Script, err)

	script, _ := hex.DecodeString(res.Result.Script)
	t := transaction.NewInvocationTX(script)
	assetID, _ := util.Uint256DecodeString(gasAssetID)
	amount, _ := util.Fixed8DecodeString("0.0001")
	addr, err := wif.PrivateKey.Address()
	if err != nil {
		panic(err)
	}

	b, err := getBalance(addr)
	if err != nil {
		panic(err)
	}
	inputs, spent := calculateInputs("", b, amount)

	t.Inputs = inputs

	pubkey, _ := wif.PrivateKey.PublicKey()
	fmt.Printf("pubkey: %x\n", pubkey)
	scriptHash := "21" + hex.EncodeToString(pubkey) + "ac"
	data, err := util.Uint160FromScript([]byte(scriptHash))
	fmt.Println("first: %x %v", data, err)
	t.Attributes = []*transaction.Attribute{{Data: data.Bytes(), Usage: transaction.Script}}

	s, _ := hex.DecodeString(scriptHash)
	data, err = util.Uint160FromScript(s)
	fmt.Println("secon: %x %v", data, err)
	t.Attributes = []*transaction.Attribute{{Data: data.Bytes(), Usage: transaction.Script}}

	p, _ := wif.PrivateKey.Address()
	bs, _ := crypto.Base58Decode(p)
	fmt.Printf("%x\n", bs)
	hash := hex.EncodeToString(bs[1:21])
	a, err := util.Uint160DecodeString(hash)
	if err != nil {
		panic(err)
	}
	t.AddOutput(&transaction.Output{assetID, spent - amount, a})

	buf := &bytes.Buffer{}
	err = t.EncodeHashableFields(buf)
	if err != nil {
		panic(err)
	}

	bb := buf.Bytes()
	fmt.Printf("%x\n", bb)
	return

	signature, err := wif.PrivateKey.Sign(bb[:len(bb)-1])
	if err != nil {
		panic(err)
	}
	fmt.Printf("sign: %x\n", signature)

	invocS, _ := hex.DecodeString("40" + hex.EncodeToString(signature))
	verifS, _ := hex.DecodeString(scriptHash)
	t.Scripts = []*transaction.Witness{{invocS, verifS}}
	t.Hash()
	spew.Dump(t)

	buf = &bytes.Buffer{}
	err = t.EncodeBinary(buf)
	if err != nil {
		panic(err)
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
