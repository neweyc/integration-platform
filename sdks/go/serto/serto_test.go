package serto

import (
	"bytes"
	"encoding/json"
	"errors"
	"strings"
	"testing"
)

const invocationJSON = `{
  "protocolVersion": "1",
  "entrypoint": "x",
  "execution": { "environment": "production", "integrationName": "Test" },
  "trigger": { "type": "manual" },
  "payload": null,
  "secrets": { "API_KEY": "xyz" }
}`

func run(t *testing.T, inv string, handler Handler) []map[string]interface{} {
	t.Helper()
	out := &bytes.Buffer{}
	runWith(strings.NewReader(inv), out, handler)

	var events []map[string]interface{}
	for _, line := range strings.Split(strings.TrimSpace(out.String()), "\n") {
		if line == "" {
			continue
		}
		var e map[string]interface{}
		if err := json.Unmarshal([]byte(line), &e); err != nil {
			t.Fatalf("emitted line is not valid JSON %q: %v", line, err)
		}
		events = append(events, e)
	}
	return events
}

func TestLogsPublishesAndSucceeds(t *testing.T) {
	events := run(t, invocationJSON, func(ctx *Context) error {
		ctx.Logger.Info("ran ok")
		return ctx.Publish("test.subject", map[string]int{"k": 1})
	})

	if !hasEvent(events, func(e map[string]interface{}) bool {
		return e["type"] == "log" && e["level"] == "Information" && e["message"] == "ran ok"
	}) {
		t.Fatalf("expected a log event, got %v", events)
	}
	if !hasEvent(events, func(e map[string]interface{}) bool {
		return e["type"] == "message" && e["subject"] == "test.subject" && strings.Contains(e["body"].(string), `"k":1`)
	}) {
		t.Fatalf("expected a message event, got %v", events)
	}

	last := events[len(events)-1]
	if last["type"] != "result" || last["succeeded"] != true {
		t.Fatalf("expected a succeeded result, got %v", last)
	}
}

func TestHandlerErrorBecomesFailedResult(t *testing.T) {
	events := run(t, invocationJSON, func(ctx *Context) error {
		return errors.New("boom")
	})

	last := events[len(events)-1]
	if last["type"] != "result" || last["succeeded"] != false {
		t.Fatalf("expected a failed result, got %v", last)
	}
	if !strings.Contains(last["error"].(string), "boom") {
		t.Fatalf("expected error to contain 'boom', got %v", last["error"])
	}
}

func TestSecretsAreAvailable(t *testing.T) {
	var seen string
	run(t, invocationJSON, func(ctx *Context) error {
		seen = ctx.Secrets["API_KEY"]
		return nil
	})
	if seen != "xyz" {
		t.Fatalf("expected secret 'xyz', got %q", seen)
	}
}

func TestPanicBecomesFailedResult(t *testing.T) {
	events := run(t, invocationJSON, func(ctx *Context) error {
		panic("kaboom")
	})
	last := events[len(events)-1]
	if last["type"] != "result" || last["succeeded"] != false {
		t.Fatalf("expected a failed result on panic, got %v", last)
	}
	if !strings.Contains(last["error"].(string), "kaboom") {
		t.Fatalf("expected panic message in error, got %v", last["error"])
	}
}

func hasEvent(events []map[string]interface{}, pred func(map[string]interface{}) bool) bool {
	for _, e := range events {
		if pred(e) {
			return true
		}
	}
	return false
}
