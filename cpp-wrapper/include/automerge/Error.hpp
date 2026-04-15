#pragma once
#ifndef AUTOMERGE_ERROR_HPP
#define AUTOMERGE_ERROR_HPP

#include <stdexcept>
#include <string>

namespace automerge {

/// Exception thrown when an Automerge C-API call fails.
class AutomergeError : public std::runtime_error {
public:
    explicit AutomergeError(const std::string& msg)
        : std::runtime_error("AutomergeError: " + msg) {}
};

} // namespace automerge

#endif // AUTOMERGE_ERROR_HPP
