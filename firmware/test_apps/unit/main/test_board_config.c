#include "unity.h"

#include "board_config.h"

TEST_CASE("NT35510 uses hardware-calibrated RGB element order", "[board][display]")
{
    TEST_ASSERT_EQUAL_INT(0, BOARD_LCD_BGR);
}
