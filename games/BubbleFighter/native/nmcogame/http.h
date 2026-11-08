#pragma once
// WinHTTP transport of the messenger stand-in ([messenger] transport = "http").
#include "friends_client.h"

#include <string>

namespace bubblefighter::nmco {

/// A sender for the service at `base_url` (http or https, optional path). Replies over 1 MiB are refused.
/// Returns an empty function and sets `error` when the URL cannot be used.
HttpSend make_http_send(const std::string& base_url, std::string& error);

} // namespace bubblefighter::nmco
