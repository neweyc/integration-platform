// Package serto is the Go SDK for writing Serto integrations. An integration is a program whose main()
// calls serto.Run with a handler:
//
//	func main() {
//	    serto.Run(func(ctx *serto.Context) error {
//	        ctx.Logger.Info("running")
//	        return ctx.Publish("orders.synced", map[string]int{"count": 42})
//	    })
//	}
//
// Go integrations ship as a container image (runtime "container"); the image's CMD is the compiled
// binary, which speaks the wire protocol over stdin/stdout. See docs/multi-language-runtimes.md.
package serto

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"os"
)

const protocolVersion = "1"

// Handler is an integration. Returning a non-nil error fails the run; the error message is reported.
type Handler func(ctx *Context) error

// Run reads the invocation from stdin, executes the handler, and emits the terminal result. It is the
// entry point a Go integration's main() calls.
//
// The integration's own stdout is redirected to stderr so a stray fmt.Println can never corrupt the
// JSON-lines protocol channel — write diagnostics freely; structured output goes through the Context.
func Run(handler Handler) {
	realStdout := os.Stdout
	os.Stdout = os.Stderr
	runWith(os.Stdin, realStdout, handler)
}

// runWith is the testable core: explicit streams, no global state.
func runWith(in io.Reader, out io.Writer, handler Handler) {
	emit := newEmitter(out)

	// A panic in the handler becomes a failed result rather than crashing the process.
	defer func() {
		if r := recover(); r != nil {
			emit(event{Type: "result", Succeeded: boolPtr(false), Error: fmt.Sprintf("panic: %v", r)})
		}
	}()

	var inv invocation
	if err := json.NewDecoder(in).Decode(&inv); err != nil {
		emit(event{Type: "result", Succeeded: boolPtr(false), Error: fmt.Sprintf("failed to read invocation: %v", err)})
		return
	}

	ctx := &Context{
		Secrets:   inv.Secrets,
		Payload:   inv.Payload,
		Trigger:   inv.Trigger,
		Execution: inv.Execution,
		emit:      emit,
	}
	ctx.Logger = &Logger{emit: emit}
	if ctx.Secrets == nil {
		ctx.Secrets = map[string]string{}
	}

	if err := handler(ctx); err != nil {
		emit(event{Type: "result", Succeeded: boolPtr(false), Error: err.Error()})
		return
	}

	emit(event{Type: "result", Succeeded: boolPtr(true)})
}

// Context is handed to a handler: secrets, a logger, the trigger, the payload, execution metadata, and a
// way to publish messages.
type Context struct {
	Secrets   map[string]string
	Payload   string
	Trigger   map[string]interface{}
	Execution Execution
	Logger    *Logger

	emit func(event)
}

// Execution holds metadata about the current run.
type Execution struct {
	ExecutionID     string `json:"executionId"`
	IntegrationID   string `json:"integrationId"`
	IntegrationName string `json:"integrationName"`
	Environment     string `json:"environment"`
	ScheduledAt     string `json:"scheduledAt"`
}

// Publish emits a message other integrations can subscribe to. A non-string body is JSON-encoded.
func (c *Context) Publish(subject string, body interface{}) error {
	var encoded string
	switch v := body.(type) {
	case string:
		encoded = v
	default:
		b, err := json.Marshal(body)
		if err != nil {
			return err
		}
		encoded = string(b)
	}
	c.emit(event{Type: "message", Subject: subject, Body: encoded})
	return nil
}

// PayloadJSON unmarshals the payload into v. It is a no-op (returns nil) when there is no payload.
func (c *Context) PayloadJSON(v interface{}) error {
	if c.Payload == "" {
		return nil
	}
	return json.Unmarshal([]byte(c.Payload), v)
}

// Logger writes structured log events. Levels match .NET LogLevel names so they render identically in
// execution history.
type Logger struct {
	emit func(event)
}

func (l *Logger) log(level, message string) { l.emit(event{Type: "log", Level: level, Message: message}) }

func (l *Logger) Trace(message string) { l.log("Trace", message) }
func (l *Logger) Debug(message string) { l.log("Debug", message) }
func (l *Logger) Info(message string)  { l.log("Information", message) }
func (l *Logger) Warn(message string)  { l.log("Warning", message) }
func (l *Logger) Error(message string) { l.log("Error", message) }

func (l *Logger) Tracef(format string, a ...interface{}) { l.Trace(fmt.Sprintf(format, a...)) }
func (l *Logger) Debugf(format string, a ...interface{}) { l.Debug(fmt.Sprintf(format, a...)) }
func (l *Logger) Infof(format string, a ...interface{})  { l.Info(fmt.Sprintf(format, a...)) }
func (l *Logger) Warnf(format string, a ...interface{})  { l.Warn(fmt.Sprintf(format, a...)) }
func (l *Logger) Errorf(format string, a ...interface{}) { l.Error(fmt.Sprintf(format, a...)) }

type invocation struct {
	ProtocolVersion string                 `json:"protocolVersion"`
	Entrypoint      string                 `json:"entrypoint"`
	Execution       Execution              `json:"execution"`
	Trigger         map[string]interface{} `json:"trigger"`
	Payload         string                 `json:"payload"`
	Secrets         map[string]string      `json:"secrets"`
}

type event struct {
	Type      string `json:"type"`
	Level     string `json:"level,omitempty"`
	Message   string `json:"message,omitempty"`
	Exception string `json:"exception,omitempty"`
	Subject   string `json:"subject,omitempty"`
	Body      string `json:"body,omitempty"`
	Succeeded *bool  `json:"succeeded,omitempty"`
	Error     string `json:"error,omitempty"`
}

// newEmitter returns a function that writes one JSON event per line, flushing each so the agent sees
// output promptly.
func newEmitter(out io.Writer) func(event) {
	writer := bufio.NewWriter(out)
	return func(e event) {
		b, err := json.Marshal(e)
		if err != nil {
			return
		}
		writer.Write(b)
		writer.WriteByte('\n')
		writer.Flush()
	}
}

func boolPtr(b bool) *bool { return &b }
