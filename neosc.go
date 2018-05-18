package main

import (
	"context"
	"fmt"

	sc "gitlab.com/casperDev/Casper-server/casper/sc"
	neo "gitlab.com/casperDev/Casper-server/casper/sc/neo"
)

func main() {
	ctx := context.Background()

	c, err := sc.GetContractByName(sc.Neo, ctx)
	if err != nil {
		panic(err)
	}

	//c.ConfirmUpload("node1", "file1", 100)
	//c.ConfirmUpload("node1", "file1", -100)
	//c.RegisterProvider("node1", "lulkek", "ip_addr", "thrift_addr", 570)
	//return
	ip, err := c.GetIpPort("node1")

	fmt.Println(ip)
	fmt.Printf("%v\n", err)
}
