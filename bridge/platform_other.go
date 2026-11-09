//go:build !windows

package main

import "time"

// Hors Windows (tests, développement) : rien à configurer.

func tryMutex() bool                     { return true }
func waitMutex(max time.Duration) bool   { return true }
func findAppProcess() (int, string)      { return 0, "" }
func focusWindowOf(pid int)              {}
func killPID(pid int)                    {}
func killTree(pid int)                   {}
func cleanupWebViews(exeName string)     {}
func startDetached(exe string) error     { return nil }
func waitPid(pid int, max time.Duration) {}
func firewallRulesPresent() bool         { return true }
func applyFirewallRules() error          { return nil }
func addFirewallRules() string           { return "ok" }
