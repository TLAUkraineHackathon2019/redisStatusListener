# redisStatusListener

This repo is a part of project. We need this to run long awaited tasks, but we can't do it synchronously. So we setup own Redis. Our services grabs data, sends statuses for each query so we can listen to them and act when data is ready.

The core of this is SubscriptionProcessor class. It runs as singletone service. SubscriptionProcessor class can register queries and callbacks which will bi fired once we got the right signal from Redis. SubscriptionProcessor has a comments, so I hope I made it easy for understanging for both programmer and non-programmer persons what these code lines do.
