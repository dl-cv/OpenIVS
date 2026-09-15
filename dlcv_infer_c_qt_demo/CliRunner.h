#pragma once

#include <QString>
#include <QStringList>

void PrintCliHelp(const QString& programPath);
int RunCliCommand(const QStringList& args);
