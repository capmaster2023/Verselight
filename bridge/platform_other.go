//go:build !windows

package main

import "time"

// Hors Windows (tests, développement) : pas de pare-feu à configurer.

func singleInstance() bool                            { return true }
func firewallRulesPresent() bool                      { return true }
func addFirewallRules(tcpFrom, tcpTo, udp int) string { return "ok" }
func killTree(pid int)                                {}
func waitPid(pid int, max time.Duration)              {}
