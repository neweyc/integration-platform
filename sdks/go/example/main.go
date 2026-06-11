package main

import serto "github.com/neweyc/integration-platform/sdks/go/serto"

func main() {
	serto.Run(func(ctx *serto.Context) error {
		ctx.Logger.Infof("Hello from Go — environment: %s", ctx.Execution.Environment)

		if key, ok := ctx.Secrets["API_KEY"]; ok {
			ctx.Logger.Infof("API_KEY is present (%d chars)", len(key))
		} else {
			ctx.Logger.Warn("API_KEY is not configured")
		}

		if ctx.Payload != "" {
			ctx.Logger.Infof("Received payload: %s", ctx.Payload)
		}

		return ctx.Publish("go.greeted", map[string]interface{}{
			"greeted":     true,
			"environment": ctx.Execution.Environment,
		})
	})
}
