package main

import (
	"bytes"
	"encoding/hex"
	"flag"
	"fmt"
	"io"
	"os"

	"github.com/CityOfZion/neo-go/pkg/core/transaction"
	"github.com/davecgh/go-spew/spew"
)

var fname = flag.String("file", "", "filename")

func main() {
	tran := &transaction.Transaction{}

	flag.Parse()

	var r io.Reader
	if *fname == "" {
		tstr, _ := hex.DecodeString(os.Args[1])
		r = bytes.NewReader(tstr)
	} else {
		r, _ = os.Open(*fname)
	}

	err := tran.DecodeBinary(r)
	if err != nil {
		fmt.Println(err)
		return
	}

	spew.Dump(tran)
}
