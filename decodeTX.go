package main

import (
	"bytes"
	"encoding/hex"
	"fmt"
	"os"

	"github.com/CityOfZion/neo-go/pkg/core/transaction"
	"github.com/davecgh/go-spew/spew"
)

func main() {
	tran := &transaction.Transaction{}
	tstr, err := hex.DecodeString(os.Args[1])
	err = tran.DecodeBinary(bytes.NewReader(tstr))
	if err != nil {
		fmt.Println(err)
		return
	}

	spew.Dump(tran)
}
