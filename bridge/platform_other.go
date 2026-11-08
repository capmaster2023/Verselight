//go:build !windows

package main

// Hors Windows (tests, développement) : pas de pare-feu à configurer.

func singleInstance() bool                            { return true }
func firewallRulesPresent() bool                      { return true }
func addFirewallRules(tcpFrom, tcpTo, udp int) string { return "ok" }
